#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Windows;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;
using Microsoft.Win32;

namespace CodexSessionManager.App;

[SuppressMessage("Code Smell", "S2333", Justification = "The class is split across XAML-generated and hand-authored partial files.")]
public partial class MainWindow : Window
{
    private readonly ObservableCollection<IndexedLogicalSession> _sessions = [];
    private readonly SearchCancellationState _searchCancellation = new();
    private SessionCatalogRepository? _repository;
    private SessionWorkspaceIndexer? _workspaceIndexer;
    private MaintenanceExecutor? _maintenanceExecutor;
    private MaintenancePreview? _currentMaintenancePreview;

    internal Func<string> LocalDataRootProvider { get; set; }
    internal Func<string, SessionCatalogRepository> RepositoryFactory { get; set; }
    internal Func<SessionCatalogRepository, SessionWorkspaceIndexer> WorkspaceIndexerFactory { get; set; }
    internal Func<string, MaintenanceExecutor> MaintenanceExecutorFactory { get; set; }
    internal Action ScheduleRefreshAction { get; set; }
    internal Func<bool, IReadOnlyList<KnownSessionStore>> KnownStoresProvider { get; set; }
    internal Func<string> LiveSqliteStatusProvider { get; set; }
    internal Func<string, CancellationToken, Task<ParsedSessionFile>> SessionParser { get; set; }
    internal Func<string, string> FileTextReader { get; set; }
    internal Action<string, IReadOnlyList<string>> ProcessStarter { get; set; }
    internal Action<string> ClipboardSetter { get; set; }
    internal Func<SaveFileDialog> SaveFileDialogFactory { get; set; }
    internal Func<SaveFileDialog, Window, bool?> SaveFileDialogPresenter { get; set; }
    internal Func<string, string?> ExportPathSelector { get; set; }
    internal Action<string, string> TextFileWriter { get; set; }
    internal Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>> MaintenanceRunner { get; set; }
    internal CancellationTokenSource? CurrentSearchCancellationTokenSource => _searchCancellation.Snapshot();

    public MainWindow()
    {
        InitializeComponent();
        SessionsListBox.ItemsSource = _sessions;
        MaintenanceActionComboBox.ItemsSource = Enum.GetValues<MaintenanceAction>();
        MaintenanceActionComboBox.SelectedItem = MaintenanceAction.Archive;
        LocalDataRootProvider = () =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexSessionManager");
        RepositoryFactory = databasePath => new SessionCatalogRepository(databasePath);
        WorkspaceIndexerFactory = repository => new SessionWorkspaceIndexer(repository);
        MaintenanceExecutorFactory = checkpointRoot => new MaintenanceExecutor(checkpointRoot);
        ScheduleRefreshAction = () => _ = RunBackgroundRefreshAsync();
        KnownStoresProvider = deepScan => BuildKnownStores(deepScan);
        LiveSqliteStatusProvider = GetLiveSqliteStatus;
        SessionParser = (filePath, cancellationToken) => SessionJsonlParser.ParseAsync(filePath, cancellationToken);
        FileTextReader = File.ReadAllText;
        ProcessStarter = static (fileName, arguments) => StartExternalProcess(fileName, arguments);
        ClipboardSetter = Clipboard.SetText;
        SaveFileDialogFactory = () => new SaveFileDialog();
        SaveFileDialogPresenter = (dialog, owner) => dialog.ShowDialog(owner);
        ExportPathSelector = SelectExportPath;
        TextFileWriter = (fileName, content) => File.WriteAllText(fileName, content, Encoding.UTF8);
        MaintenanceRunner = (preview, destinationRoot, typedConfirmation, cancellationToken) =>
        {
            var maintenanceExecutor = _maintenanceExecutor
                ?? throw new InvalidOperationException("Maintenance executor has not been initialized.");
            return maintenanceExecutor.ExecuteAsync(preview, destinationRoot, typedConfirmation, cancellationToken);
        };
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => ReleaseSearchCancellationState();
    }

    private static void StartExternalProcess(string fileName, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(fileName));
        }

        var processArguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        var normalizedFileName = NormalizeAllowedProcessFileName(fileName);
        var startInfo = new ProcessStartInfo(normalizedFileName)
        {
            UseShellExecute = false,
        };

        foreach (var argument in processArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _ = Process.Start(startInfo);
    }

    private static string NormalizeAllowedProcessFileName(string fileName)
    {
        var candidate = fileName ?? throw new ArgumentNullException(nameof(fileName));
        if (string.Equals(candidate, "explorer.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "notepad.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        var systemDirectory = Environment.SystemDirectory;
        if (Path.IsPathRooted(candidate)
            && string.Equals(Path.GetDirectoryName(candidate), systemDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var executableName = Path.GetFileName(candidate);
            if (string.Equals(executableName, "cmd.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(executableName, "whoami.exe", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Launching '{candidate}' is not allowed.");
    }

    private async Task InitializeAsync()
    {
        try
        {
            var localDataRoot = LocalDataRootProvider();
            Directory.CreateDirectory(localDataRoot);
            await RunOnUiThreadAsync(() => DestinationRootTextBox.Text = Path.Combine(localDataRoot, "maintenance", "archive"));

            _repository = RepositoryFactory(Path.Combine(localDataRoot, "catalog.db"));
            _workspaceIndexer = WorkspaceIndexerFactory(_repository);
            _maintenanceExecutor = MaintenanceExecutorFactory(Path.Combine(localDataRoot, "checkpoints"));

            await _repository.InitializeAsync(CancellationToken.None);
            await LoadSessionsFromCatalogAsync();

            ScheduleRefreshAction();
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Startup failed: {ex.Message}");
        }
    }

    private async Task RunBackgroundRefreshAsync()
    {
        try
        {
            await RefreshAsync(deepScan: false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Background refresh failed: {ex.Message}");
        }
    }

    private async Task LoadSessionsFromCatalogAsync()
    {
        if (_repository is null)
        {
            return;
        }

        var sessions = await _repository.ListSessionsAsync(CancellationToken.None);

        await RunOnUiThreadAsync(() =>
        {
            _sessions.Clear();
            foreach (var session in sessions)
            {
                _sessions.Add(session);
            }

            StatusTextBlock.Text = $"Loaded {_sessions.Count} sessions from cached index.";
        });
    }

    private async Task RefreshAsync(bool deepScan)
    {
        if (_repository is null || _workspaceIndexer is null)
        {
            return;
        }

        await RunOnUiThreadAsync(() => StatusTextBlock.Text = deepScan ? "Running deep scan…" : "Refreshing known stores…");

        var workspaceIndexer = _workspaceIndexer;
        var knownStores = GetKnownStores(KnownStoresProvider, deepScan);
        await workspaceIndexer.RebuildAsync(knownStores, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();

        await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Indexed {_sessions.Count} deduped sessions at {DateTime.UtcNow.ToLocalTime():t}.");
    }

    private string? SelectExportPath(string defaultFileName)
    {
        var dialog = SaveFileDialogFactory() ?? throw new InvalidOperationException("Save file dialog factory returned null.");

        dialog.FileName = defaultFileName;
        dialog.Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt|JSON (*.json)|*.json";
        return SaveFileDialogPresenter(dialog, this) == true ? dialog.FileName : null;
    }

    private IndexedLogicalSession? GetSelectedSession() => SessionsListBox.SelectedItem as IndexedLogicalSession;

    private IReadOnlyList<IndexedLogicalSession> GetSelectedSessions() =>
        SessionsListBox.SelectedItems.Cast<IndexedLogicalSession>().ToArray();

    private void SessionsListBox_OnSelectionChanged(object _, System.Windows.Controls.SelectionChangedEventArgs __) =>
        RunEventTask(LoadSelectedSessionAsync, "Failed to load session");

    private void SearchTextBox_OnTextChanged(object _, System.Windows.Controls.TextChangedEventArgs __) =>
        RunEventTask(SearchSessionsAsync, "Failed to search sessions");

    [ExcludeFromCodeCoverage]
    private void RefreshButton_OnClick(object _, RoutedEventArgs __) =>
        RunEventTask(() => RefreshAsync(deepScan: false), "Refresh failed");

    [ExcludeFromCodeCoverage]
    private void DeepScanButton_OnClick(object _, RoutedEventArgs __) =>
        RunEventTask(() => RefreshAsync(deepScan: true), "Deep scan failed");

    private async Task SaveSelectedMetadataAsync()
    {
        var repository = _repository;
        if (repository is null)
        {
            return;
        }

        var metadata = await RunOnUiThreadValueAsync(() => (
            Selected: GetSelectedSession(),
            Alias: AliasTextBox.Text,
            TagsText: TagsTextBox.Text,
            Notes: NotesTextBox.Text));
        var selected = metadata.Selected;
        if (selected is null)
        {
            return;
        }

        var tags = metadata.TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        var sessionId = RequireSelectedSessionId(selected.SessionId);

        await repository.UpdateMetadataAsync(sessionId, metadata.Alias, tags, metadata.Notes, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();
        await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Saved metadata for {sessionId}.");
    }

    [ExcludeFromCodeCoverage]
    private void SaveMetadataButton_OnClick(object _, RoutedEventArgs __) =>
        RunEventTask(SaveSelectedMetadataAsync, "Failed to save metadata");

    private void OpenFolderButton_OnClick(object _, RoutedEventArgs __)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var preferredPath = GetRequiredPreferredCopy(selected).FilePath;
        var folder = Path.GetDirectoryName(preferredPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            ProcessStarter("explorer.exe", [folder]);
        }
    }

    private void OpenRawButton_OnClick(object _, RoutedEventArgs __)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var preferredPath = GetRequiredPreferredCopy(selected).FilePath;
        ProcessStarter("notepad.exe", [preferredPath]);
    }

    private void CopyPathButton_OnClick(object _, RoutedEventArgs __)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var preferredPath = GetRequiredPreferredCopy(selected).FilePath;
        ClipboardSetter(preferredPath);
        StatusTextBlock.Text = "Copied preferred path to clipboard.";
    }

    private void ResumeButton_OnClick(object _, RoutedEventArgs __)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var sessionId = RequireSelectedSessionId(selected.SessionId);
        var cwd = !string.IsNullOrWhiteSpace(CwdTextBlock.Text) && CwdTextBlock.Text != "-" ? CwdTextBlock.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        ProcessStarter("codex", ["resume", sessionId, "-C", cwd]);
        StatusTextBlock.Text = $"Opened Codex resume command for {sessionId}.";
    }

    private void ExportButton_OnClick(object _, RoutedEventArgs __)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var exportPath = ExportPathSelector($"{selected.SessionId}.md");
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        TextFileWriter(exportPath, ReadableTranscriptTextBox.Text);
        StatusTextBlock.Text = $"Exported session to {exportPath}.";
    }

    private void BuildPreviewButton_OnClick(object _, RoutedEventArgs __)
    {
        var selectedSessions = GetSelectedSessions();
        if (selectedSessions.Count == 0)
        {
            return;
        }

        var targets = selectedSessions.SelectMany(static session => session.PhysicalCopies ?? []).ToArray();
        var action = MaintenanceActionComboBox.SelectedItem is MaintenanceAction selectedAction
            ? selectedAction
            : MaintenanceAction.Archive;
        var confirmation = $"{action.ToString().ToUpperInvariant()} {targets.Length} FILE{(targets.Length == 1 ? string.Empty : "S")}";

        _currentMaintenancePreview = MaintenancePlanner.CreatePreview(new MaintenanceRequest(action, targets, confirmation));
        MaintenanceSummaryTextBlock.Text = $"Allowed: {_currentMaintenancePreview.AllowedTargets.Count} | Blocked: {_currentMaintenancePreview.BlockedTargets.Count} | Confirm with: {confirmation}";
        MaintenanceWarningsTextBox.Text = string.Join(Environment.NewLine, _currentMaintenancePreview.Warnings.Select(w => $"[{w.Severity}] {w.Message}"));
        TypedConfirmationTextBox.Text = confirmation;
    }

    private async Task ExecuteMaintenanceAsync()
    {
        if (_currentMaintenancePreview is null || _maintenanceExecutor is null)
        {
            return;
        }

        string destinationRoot = string.Empty;
        string typedConfirmation = string.Empty;
        await RunOnUiThreadAsync(() =>
        {
            destinationRoot = DestinationRootTextBox.Text;
            typedConfirmation = TypedConfirmationTextBox.Text;
        });

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            destinationRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexSessionManager",
                "maintenance",
                _currentMaintenancePreview.Action.ToString().ToLowerInvariant());
        }

        try
        {
            var result = await MaintenanceRunner(_currentMaintenancePreview, destinationRoot, typedConfirmation, CancellationToken.None);
            await RunOnUiThreadAsync(() => StatusTextBlock.Text = result.Executed
                ? $"Executed maintenance. Checkpoint: {result.ManifestPath}"
                : "Maintenance did not execute.");
            await RefreshAsync(deepScan: false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Maintenance failed: {ex.Message}");
        }
    }

    [ExcludeFromCodeCoverage]
    private void ExecuteMaintenanceButton_OnClick(object _, RoutedEventArgs __) =>
        RunEventTask(ExecuteMaintenanceAsync, "Maintenance failed");
}
