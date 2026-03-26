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

public partial class MainWindow : Window
{
    private readonly ObservableCollection<IndexedLogicalSession> _sessions = [];
    private SessionCatalogRepository? _repository;
    private SessionWorkspaceIndexer? _workspaceIndexer;
    private MaintenanceExecutor? _maintenanceExecutor;
    private MaintenancePreview? _currentMaintenancePreview;
    private CancellationTokenSource? _searchCts;

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
            _maintenanceExecutor!.ExecuteAsync(preview, destinationRoot, typedConfirmation, cancellationToken);
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

        var knownStores = KnownStoresProvider(deepScan);
        await _workspaceIndexer.RebuildAsync(knownStores, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();

        await RunOnUiThreadAsync(() =>
        {
            StatusTextBlock.Text = $"Indexed {_sessions.Count} deduped sessions at {DateTime.Now:t}.";
        });
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> RunOnUiThreadValueAsync<T>(Func<T> func)
    {
        if (Dispatcher.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return Dispatcher.InvokeAsync(func).Task;
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

    private async void SessionsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        await LoadSelectedSessionAsync();

    private async void SearchTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        await SearchSessionsAsync();

    [ExcludeFromCodeCoverage]
    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: false);

    [ExcludeFromCodeCoverage]
    private async void DeepScanButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: true);

    private async Task SaveSelectedMetadataAsync()
    {
        if (_repository is null)
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

        await _repository.UpdateMetadataAsync(selected.SessionId, metadata.Alias, tags, metadata.Notes, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();
        await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"Saved metadata for {selected.SessionId}.");
    }

    [ExcludeFromCodeCoverage]
    private async void SaveMetadataButton_OnClick(object sender, RoutedEventArgs e) =>
        await SaveSelectedMetadataAsync();

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(selected.PreferredCopy.FilePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            ProcessStarter("explorer.exe", $"\"{folder}\"");
        }
    }

    private void OpenRawButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        ProcessStarter("notepad.exe", $"\"{selected.PreferredCopy.FilePath}\"");
    }

    private void CopyPathButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        ClipboardSetter(selected.PreferredCopy.FilePath);
        StatusTextBlock.Text = "Copied preferred path to clipboard.";
    }

    private void ResumeButton_OnClick(object sender, RoutedEventArgs e)
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

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
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

    private void BuildPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedSessions = GetSelectedSessions();
        if (selectedSessions.Count == 0)
        {
            return;
        }

        var targets = selectedSessions.SelectMany(session => session.PhysicalCopies).ToArray();
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
    private async void ExecuteMaintenanceButton_OnClick(object sender, RoutedEventArgs e) =>
        await ExecuteMaintenanceAsync();

    internal static string? DescribeSqlitePath(string path, Func<string, FileInfo>? fileInfoFactory = null)
    {
        try
        {
            var info = (fileInfoFactory ?? (static filePath => new FileInfo(filePath)))(path);
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
}
