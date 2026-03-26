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
    private readonly MaintenancePlanner _maintenancePlanner = new();
    private SessionCatalogRepository? _repository;
    private SessionWorkspaceIndexer? _workspaceIndexer;
    private MaintenanceExecutor? _maintenanceExecutor;
    private MaintenancePreview? _currentMaintenancePreview;

    internal Func<string> LocalDataRootProvider { get; set; } = null!;
    internal Func<string, SessionCatalogRepository> RepositoryFactory { get; set; } = null!;
    internal Func<SessionCatalogRepository, SessionWorkspaceIndexer> WorkspaceIndexerFactory { get; set; } = null!;
    internal Func<string, MaintenanceExecutor> MaintenanceExecutorFactory { get; set; } = null!;
    internal Action ScheduleRefreshAction { get; set; } = null!;
    internal Func<bool, IReadOnlyList<KnownSessionStore>> KnownStoresProvider { get; set; } = null!;
    internal Func<string> LiveSqliteStatusProvider { get; set; } = null!;
    internal Func<string, CancellationToken, Task<ParsedSessionFile>> SessionParser { get; set; } = null!;
    internal Func<string, string> FileTextReader { get; set; } = null!;
    internal Action<string, string> ProcessStarter { get; set; } = null!;
    internal Action<string> ClipboardSetter { get; set; } = null!;
    internal Func<SaveFileDialog> SaveFileDialogFactory { get; set; } = null!;
    internal Func<SaveFileDialog, Window, bool?> SaveFileDialogPresenter { get; set; } = null!;
    internal Func<string, string?> ExportPathSelector { get; set; } = null!;
    internal Action<string, string> TextFileWriter { get; set; } = null!;
    internal Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>> MaintenanceRunner { get; set; } = null!;

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
        ScheduleRefreshAction = () => _ = Task.Run(async () => await RefreshAsync(deepScan: false));
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
            foreach (var store in KnownStoreLocator.GetKnownStores(directory))
            {
                if (stores.All(existing => !string.Equals(existing.SessionsPath, store.SessionsPath, StringComparison.OrdinalIgnoreCase)))
                {
                    stores.Add(store);
                }
            }
        }

        return stores;
    }

    private IndexedLogicalSession? GetSelectedSession() => SessionsListBox.SelectedItem as IndexedLogicalSession;

    private IReadOnlyList<IndexedLogicalSession> GetSelectedSessions() =>
        SessionsListBox.SelectedItems.Cast<IndexedLogicalSession>().ToArray();

    private async Task LoadSelectedSessionAsync()
    {
        IndexedLogicalSession? selected = null;
        await RunOnUiThreadAsync(() => selected = GetSelectedSession());
        if (selected is null)
        {
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            ThreadNameTextBlock.Text = selected.ThreadName;
            SessionIdTextBlock.Text = selected.SessionId;
            PreferredPathTextBlock.Text = selected.PreferredCopy.FilePath;
            AliasTextBox.Text = selected.SearchDocument.Alias;
            TagsTextBox.Text = string.Join(", ", selected.SearchDocument.Tags);
            NotesTextBox.Text = selected.SearchDocument.Notes;
            CopiesListBox.ItemsSource = selected.PhysicalCopies;
            ReadableTranscriptTextBox.Text = selected.SearchDocument.ReadableTranscript;
            DialogueTranscriptTextBox.Text = selected.SearchDocument.DialogueTranscript;
        });

        try
        {
            var parsed = await SessionParser(selected.PreferredCopy.FilePath, CancellationToken.None);
            var rawContent = FileTextReader(selected.PreferredCopy.FilePath);
            var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            var auditTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            var sqliteStatus = LiveSqliteStatusProvider();

            await RunOnUiThreadAsync(() =>
            {
                SQLiteStatusTextBlock.Text = sqliteStatus;
                CwdTextBlock.Text = parsed.Cwd ?? "-";
                RawTranscriptTextBox.Text = rawContent;
                ReadableTranscriptTextBox.Text = readableTranscript;
                DialogueTranscriptTextBox.Text = dialogueTranscript;
                AuditTranscriptTextBox.Text = auditTranscript;
            });
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                CwdTextBlock.Text = "-";
                SQLiteStatusTextBlock.Text = "Live SQLite status unavailable.";
                AuditTranscriptTextBox.Text = string.Empty;
                RawTranscriptTextBox.Text = $"Unable to load raw session content.{Environment.NewLine}{ex.Message}";
            });
        }
    }

    private async void SessionsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        await LoadSelectedSessionAsync();

    private async Task SearchSessionsAsync()
    {
        if (_repository is null)
        {
            return;
        }

        string? query = null;
        await RunOnUiThreadAsync(() => query = SearchTextBox.Text);

        if (string.IsNullOrWhiteSpace(query))
        {
            await LoadSessionsFromCatalogAsync();
            return;
        }

        var hits = await _repository.SearchAsync(query, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await _repository.ListSessionsAsync(CancellationToken.None);

        var visibleSessions = allSessions.Where(session => hitIds.Contains(session.SessionId)).ToArray();

        await RunOnUiThreadAsync(() =>
        {
            _sessions.Clear();
            foreach (var session in visibleSessions)
            {
                _sessions.Add(session);
            }

            StatusTextBlock.Text = $"Search returned {_sessions.Count} sessions.";
        });
    }

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

        IndexedLogicalSession? selected = null;
        string alias = string.Empty;
        string tagsText = string.Empty;
        string notes = string.Empty;
        await RunOnUiThreadAsync(() =>
        {
            selected = GetSelectedSession();
            alias = AliasTextBox.Text;
            tagsText = TagsTextBox.Text;
            notes = NotesTextBox.Text;
        });

        if (selected is null)
        {
            return;
        }

        var tags = tagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        await _repository.UpdateMetadataAsync(selected.SessionId, alias, tags, notes, CancellationToken.None);
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

        _currentMaintenancePreview = _maintenancePlanner.CreatePreview(new MaintenanceRequest(action, targets, confirmation));
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
        var sqlitePaths = new[]
        {
            Path.Combine(codexHome, "state_5.sqlite"),
            Path.Combine(codexHome, "codex-sqlite", "canonical", "state_5.sqlite")
        };

        var details = sqlitePaths
            .Select(path => DescribeSqlitePath(path, fileInfoFactory: null))
            .Where(detail => detail is not null)
            .Cast<string>()
            .ToArray();

        return details.Length == 0
            ? "No live SQLite store detected."
            : string.Join(Environment.NewLine, details);
    }
}
