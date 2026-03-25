using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private Func<string>? _localDataRootOverride;
    private Action? _queueInitialRefreshOverride;
    private Func<string>? _liveSqliteStatusOverride;
    private Func<string, CancellationToken, Task<ParsedSessionFile>>? _parseSessionFileAsyncOverride;
    private Func<string, string>? _readRawFileTextOverride;
    private Func<NormalizedSessionDocument, TranscriptMode, TranscriptRenderResult>? _renderTranscriptOverride;
    private Func<ProcessStartInfo, Process?>? _launchProcessOverride;
    private Action<string>? _setClipboardTextOverride;
    private Func<SaveFileDialog, bool?>? _showSaveFileDialogOverride;
    private Action<string, string, Encoding>? _writeTextFileOverride;
    private Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>? _executeMaintenancePreviewAsyncOverride;
    private Func<Action, Task>? _invokeOnUiAsyncOverride;

    public MainWindow()
    {
        InitializeComponent();
        SessionsListBox.ItemsSource = _sessions;
        MaintenanceActionComboBox.ItemsSource = Enum.GetValues<MaintenanceAction>();
        MaintenanceActionComboBox.SelectedItem = MaintenanceAction.Archive;
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var localDataRoot = GetLocalDataRoot();
            Directory.CreateDirectory(localDataRoot);
            DestinationRootTextBox.Text = Path.Combine(localDataRoot, "maintenance", "archive");

            _repository = new SessionCatalogRepository(Path.Combine(localDataRoot, "catalog.db"));
            _workspaceIndexer = new SessionWorkspaceIndexer(_repository);
            _maintenanceExecutor = new MaintenanceExecutor(Path.Combine(localDataRoot, "checkpoints"));

            await _repository.InitializeAsync(CancellationToken.None);
            await LoadSessionsFromCatalogAsync();

            QueueInitialRefresh();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Startup failed: {ex.Message}";
        }
    }

    private async Task LoadSessionsFromCatalogAsync()
    {
        if (_repository is null)
        {
            return;
        }

        var sessions = await _repository.ListSessionsAsync(CancellationToken.None);

        await InvokeOnUiAsync(() =>
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

        await InvokeOnUiAsync(() => StatusTextBlock.Text = deepScan ? "Running deep scan…" : "Refreshing known stores…");

        var knownStores = BuildKnownStores(deepScan);
        await _workspaceIndexer.RebuildAsync(knownStores, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();

        await InvokeOnUiAsync(() =>
        {
            StatusTextBlock.Text = $"Indexed {_sessions.Count} deduped sessions at {DateTime.Now:t}.";
        });
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

    private async Task HandleSessionSelectionChangedAsync()
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        ThreadNameTextBlock.Text = selected.ThreadName;
        SessionIdTextBlock.Text = selected.SessionId;
        PreferredPathTextBlock.Text = selected.PreferredCopy.FilePath;
        AliasTextBox.Text = selected.SearchDocument.Alias;
        TagsTextBox.Text = string.Join(", ", selected.SearchDocument.Tags);
        NotesTextBox.Text = selected.SearchDocument.Notes;
        CopiesListBox.ItemsSource = selected.PhysicalCopies;
        ReadableTranscriptTextBox.Text = selected.SearchDocument.ReadableTranscript;
        DialogueTranscriptTextBox.Text = selected.SearchDocument.DialogueTranscript;

        try
        {
            SQLiteStatusTextBlock.Text = GetLiveSqliteStatusText();
            var parsed = await ParseSessionFileAsync(selected.PreferredCopy.FilePath, CancellationToken.None);
            await InvokeOnUiAsync(() =>
            {
                CwdTextBlock.Text = parsed.Cwd ?? "-";
                RawTranscriptTextBox.Text = ReadRawFileText(selected.PreferredCopy.FilePath);
                ReadableTranscriptTextBox.Text = RenderTranscript(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
                DialogueTranscriptTextBox.Text = RenderTranscript(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
                AuditTranscriptTextBox.Text = RenderTranscript(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            });
        }
        catch (Exception ex)
        {
            await InvokeOnUiAsync(() =>
            {
                CwdTextBlock.Text = "-";
                SQLiteStatusTextBlock.Text = "Live SQLite status unavailable.";
                AuditTranscriptTextBox.Text = string.Empty;
                RawTranscriptTextBox.Text = $"Unable to load raw session content.{Environment.NewLine}{ex.Message}";
            });
        }
    }

    private async void SessionsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        await HandleSessionSelectionChangedAsync();

    private async Task HandleSearchTextChangedAsync()
    {
        if (_repository is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            await LoadSessionsFromCatalogAsync();
            return;
        }

        var hits = await _repository.SearchAsync(SearchTextBox.Text, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await _repository.ListSessionsAsync(CancellationToken.None);

        await InvokeOnUiAsync(() =>
        {
            _sessions.Clear();
            foreach (var session in allSessions.Where(session => hitIds.Contains(session.SessionId)))
            {
                _sessions.Add(session);
            }

            StatusTextBlock.Text = $"Search returned {_sessions.Count} sessions.";
        });
    }

    private async void SearchTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        await HandleSearchTextChangedAsync();

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: false);

    private async void DeepScanButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: true);

    private async Task SaveMetadataAsyncCore()
    {
        if (_repository is null)
        {
            return;
        }

        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var tags = TagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        await _repository.UpdateMetadataAsync(selected.SessionId, AliasTextBox.Text, tags, NotesTextBox.Text, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();
        await InvokeOnUiAsync(() => StatusTextBlock.Text = $"Saved metadata for {selected.SessionId}.");
    }

    private async void SaveMetadataButton_OnClick(object sender, RoutedEventArgs e) =>
        await SaveMetadataAsyncCore();

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
            LaunchProcess(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    private void OpenRawButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        LaunchProcess(new ProcessStartInfo("notepad.exe", $"\"{selected.PreferredCopy.FilePath}\"") { UseShellExecute = true });
    }

    private void CopyPathButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        SetClipboardText(selected.PreferredCopy.FilePath);
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

        LaunchProcess(new ProcessStartInfo("pwsh.exe", $"-NoExit -Command \"{command}\"") { UseShellExecute = true });
        StatusTextBlock.Text = $"Opened Codex resume command for {selected.SessionId}.";
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var dialog = CreateExportDialog(selected);

        if (ShowSaveFileDialog(dialog) != true)
        {
            return;
        }

        WriteTextFile(dialog.FileName, ReadableTranscriptTextBox.Text, Encoding.UTF8);
        StatusTextBlock.Text = $"Exported session to {dialog.FileName}.";
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

    private async Task ExecuteMaintenanceAsyncCore()
    {
        if (_currentMaintenancePreview is null)
        {
            return;
        }

        var destinationRoot = DestinationRootTextBox.Text;
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
            var result = await ExecuteMaintenancePreviewAsync(_currentMaintenancePreview, destinationRoot, TypedConfirmationTextBox.Text, CancellationToken.None);
            await InvokeOnUiAsync(() =>
            {
                StatusTextBlock.Text = result.Executed
                    ? $"Executed maintenance. Checkpoint: {result.ManifestPath}"
                    : "Maintenance did not execute.";
            });
            await RefreshAsync(deepScan: false);
        }
        catch (Exception ex)
        {
            await InvokeOnUiAsync(() => StatusTextBlock.Text = $"Maintenance failed: {ex.Message}");
        }
    }

    private async void ExecuteMaintenanceButton_OnClick(object sender, RoutedEventArgs e) =>
        await ExecuteMaintenanceAsyncCore();

    protected virtual string GetLocalDataRoot() =>
        _localDataRootOverride?.Invoke()
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexSessionManager");

    protected virtual void QueueInitialRefresh() =>
        (_queueInitialRefreshOverride is not null
            ? _queueInitialRefreshOverride
            : () => _ = Task.Run(async () => await RefreshAsync(deepScan: false)))();

    protected virtual string GetLiveSqliteStatusText() =>
        _liveSqliteStatusOverride?.Invoke()
        ?? GetLiveSqliteStatus();

    protected virtual Task<ParsedSessionFile> ParseSessionFileAsync(string filePath, CancellationToken cancellationToken) =>
        _parseSessionFileAsyncOverride?.Invoke(filePath, cancellationToken)
        ?? SessionJsonlParser.ParseAsync(filePath, cancellationToken);

    protected virtual string ReadRawFileText(string filePath) =>
        _readRawFileTextOverride?.Invoke(filePath)
        ?? File.ReadAllText(filePath);

    protected virtual TranscriptRenderResult RenderTranscript(NormalizedSessionDocument document, TranscriptMode mode) =>
        _renderTranscriptOverride?.Invoke(document, mode)
        ?? SessionTranscriptFormatter.Format(document, mode);

    protected virtual Process? LaunchProcess(ProcessStartInfo startInfo) =>
        _launchProcessOverride?.Invoke(startInfo)
        ?? Process.Start(startInfo);

    protected virtual void SetClipboardText(string value)
    {
        if (_setClipboardTextOverride is not null)
        {
            _setClipboardTextOverride(value);
            return;
        }

        Clipboard.SetText(value);
    }

    protected virtual SaveFileDialog CreateExportDialog(IndexedLogicalSession selected) =>
        new()
        {
            FileName = $"{selected.SessionId}.md",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt|JSON (*.json)|*.json"
        };

    protected virtual bool? ShowSaveFileDialog(SaveFileDialog dialog) =>
        _showSaveFileDialogOverride?.Invoke(dialog)
        ?? dialog.ShowDialog(this);

    protected virtual void WriteTextFile(string path, string content, Encoding encoding) =>
        (_writeTextFileOverride ?? File.WriteAllText)(path, content, encoding);

    protected virtual Task<MaintenanceExecutionResult> ExecuteMaintenancePreviewAsync(
        MaintenancePreview preview,
        string destinationRoot,
        string typedConfirmation,
        CancellationToken cancellationToken)
    {
        if (_executeMaintenancePreviewAsyncOverride is not null)
        {
            return _executeMaintenancePreviewAsyncOverride(preview, destinationRoot, typedConfirmation, cancellationToken);
        }

        if (_maintenanceExecutor is null)
        {
            throw new InvalidOperationException("Maintenance executor is unavailable.");
        }

        return _maintenanceExecutor.ExecuteAsync(preview, destinationRoot, typedConfirmation, cancellationToken);
    }

    protected virtual Task InvokeOnUiAsync(Action action) =>
        _invokeOnUiAsyncOverride?.Invoke(action)
        ?? Dispatcher.InvokeAsync(action).Task;

    private static string GetLiveSqliteStatus()
    {
        var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var sqlitePaths = new[]
        {
            Path.Combine(codexHome, "state_5.sqlite"),
            Path.Combine(codexHome, "codex-sqlite", "canonical", "state_5.sqlite")
        };

        var details = sqlitePaths
            .Select(path =>
            {
                try
                {
                    var info = new FileInfo(path);
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
            })
            .Where(detail => detail is not null)
            .Cast<string>()
            .ToArray();

        return details.Length == 0
            ? "No live SQLite store detected."
            : string.Join(Environment.NewLine, details);
    }
}
