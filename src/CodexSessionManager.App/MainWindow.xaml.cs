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
    internal Action<string, string> ProcessStarter { get; set; }
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
        ProcessStarter = (fileName, arguments) =>
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
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
        Closed += (_, _) => DisposeSearchCancellation();
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

        var knownStoresProvider = KnownStoresProvider;
        if (knownStoresProvider is null)
        {
            throw new InvalidOperationException("Known stores provider has not been initialized.");
        }

        var workspaceIndexer = _workspaceIndexer;
        var knownStores = knownStoresProvider(deepScan);
        await workspaceIndexer.RebuildAsync(knownStores, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();

        await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Indexed {_sessions.Count} deduped sessions at {DateTime.UtcNow.ToLocalTime():t}.");
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> RunOnUiThreadValueAsync<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return dispatcher.InvokeAsync(func).Task;
    }

    private void RunEventTask(Func<Task> action, string failurePrefix)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (string.IsNullOrWhiteSpace(failurePrefix))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(failurePrefix));
        }

        _ = RunEventTaskCoreAsync(action, failurePrefix);
    }

    private async Task RunEventTaskCoreAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"{failurePrefix}: {ex.Message}");
        }
    }

    private string? SelectExportPath(string defaultFileName)
    {
        var dialog = SaveFileDialogFactory();

        dialog.FileName = defaultFileName;
        dialog.Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt|JSON (*.json)|*.json";
        return SaveFileDialogPresenter(dialog, this) == true ? dialog.FileName : null;
    }

    private static IReadOnlyList<KnownSessionStore> BuildKnownStores(bool deepScan)
    {
        var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var stores = new List<KnownSessionStore>(KnownStoreLocator.GetKnownStores(codexHome));

        if (!deepScan)
        {
            return stores;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var directory in Directory.EnumerateDirectories(userProfile, ".codex*", SearchOption.TopDirectoryOnly))
        {
            foreach (var store in KnownStoreLocator.GetKnownStores(directory)
                         .Where(store => stores.All(existing =>
                             !string.Equals(existing.SessionsPath, store.SessionsPath, StringComparison.OrdinalIgnoreCase))))
            {
                stores.Add(store);
            }
        }

        return stores;
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

        await repository.UpdateMetadataAsync(selected.SessionId, metadata.Alias, tags, metadata.Notes, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();
        await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Saved metadata for {selected.SessionId}.");
    }

    [ExcludeFromCodeCoverage]
    private void SaveMetadataButton_OnClick(object _, RoutedEventArgs __) =>
        RunEventTask(SaveSelectedMetadataAsync, "Failed to save metadata");

    private static SessionPhysicalCopy GetRequiredPreferredCopy(IndexedLogicalSession? session)
    {
        var selectedSession = session ?? throw new ArgumentNullException(nameof(session));
        return selectedSession.PreferredCopy ?? throw new InvalidOperationException("Selected session is missing a preferred copy.");
    }

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
            ProcessStarter("explorer.exe", $"\"{folder}\"");
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
        ProcessStarter("notepad.exe", $"\"{preferredPath}\"");
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

        var cwd = !string.IsNullOrWhiteSpace(CwdTextBlock.Text) && CwdTextBlock.Text != "-" ? CwdTextBlock.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var command = $"codex resume {selected.SessionId} -C \"{cwd}\"";

        ProcessStarter("pwsh.exe", $"-NoExit -Command \"{command}\"");
        StatusTextBlock.Text = $"Opened Codex resume command for {selected.SessionId}.";
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

    internal static string? DescribeSqlitePath(string path, Func<string, FileInfo>? fileInfoFactory = null)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        try
        {
            var resolvedFileInfoFactory = fileInfoFactory ?? (static filePath => new FileInfo(filePath));
            var info = resolvedFileInfoFactory(path);
            if (!info.Exists)
            {
                return null;
            }

            return $"{path} | {Math.Round(info.Length / 1024.0 / 1024.0, 1)} MB | {info.LastWriteTime}";
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetLiveSqliteStatus()
    {
        var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return GetLiveSqliteStatus(
            new[]
            {
                Path.Combine(codexHome, "state_5.sqlite"),
                Path.Combine(codexHome, "codex-sqlite", "canonical", "state_5.sqlite")
            },
            path => DescribeSqlitePath(path, fileInfoFactory: null));
    }

    private static string GetLiveSqliteStatus(IEnumerable<string> sqlitePaths, Func<string, string?> describeSqlitePath)
    {
        var details = sqlitePaths
            .Select(describeSqlitePath)
            .Where(detail => detail is not null)
            .Cast<string>()
            .ToArray();

        return details.Length == 0
            ? "No live SQLite store detected."
            : string.Join(Environment.NewLine, details);
    }

    private sealed class SearchCancellationState
    {
        private CancellationTokenSource? _current;

        public CancellationToken Begin()
        {
            var replacement = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _current, replacement);
            previous?.Cancel();
            previous?.Dispose();
            return replacement.Token;
        }

        public CancellationTokenSource? Snapshot() => Volatile.Read(ref _current);

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _current, null);
            current?.Cancel();
            current?.Dispose();
        }
    }
}

