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
            var localDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexSessionManager");
            Directory.CreateDirectory(localDataRoot);
            DestinationRootTextBox.Text = Path.Combine(localDataRoot, "maintenance", "archive");

            _repository = new SessionCatalogRepository(Path.Combine(localDataRoot, "catalog.db"));
            _workspaceIndexer = new SessionWorkspaceIndexer(_repository);
            _maintenanceExecutor = new MaintenanceExecutor(Path.Combine(localDataRoot, "checkpoints"));

            await _repository.InitializeAsync(CancellationToken.None);
            await LoadSessionsFromCatalogAsync();

            _ = Task.Run(async () => await RefreshAsync(deepScan: false));
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

        await Dispatcher.InvokeAsync(() =>
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

        await Dispatcher.InvokeAsync(() => StatusTextBlock.Text = deepScan ? "Running deep scan…" : "Refreshing known stores…");

        var knownStores = BuildKnownStores(deepScan);
        await _workspaceIndexer.RebuildAsync(knownStores, CancellationToken.None);
        await LoadSessionsFromCatalogAsync();

        await Dispatcher.InvokeAsync(() =>
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

    private async void SessionsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
        SQLiteStatusTextBlock.Text = GetLiveSqliteStatus();

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(selected.PreferredCopy.FilePath, CancellationToken.None);
            CwdTextBlock.Text = parsed.Cwd ?? "-";
            RawTranscriptTextBox.Text = File.ReadAllText(selected.PreferredCopy.FilePath);
            ReadableTranscriptTextBox.Text = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            DialogueTranscriptTextBox.Text = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            AuditTranscriptTextBox.Text = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
        }
        catch (Exception ex)
        {
            CwdTextBlock.Text = "-";
            AuditTranscriptTextBox.Text = string.Empty;
            RawTranscriptTextBox.Text = $"Unable to load raw session content.{Environment.NewLine}{ex.Message}";
        }
    }

    private async void SearchTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
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

        _sessions.Clear();
        foreach (var session in allSessions.Where(session => hitIds.Contains(session.SessionId)))
        {
            _sessions.Add(session);
        }

        StatusTextBlock.Text = $"Search returned {_sessions.Count} sessions.";
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: false);

    private async void DeepScanButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(deepScan: true);

    private async void SaveMetadataButton_OnClick(object sender, RoutedEventArgs e)
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
        StatusTextBlock.Text = $"Saved metadata for {selected.SessionId}.";
    }

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
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    private void OpenRawButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{selected.PreferredCopy.FilePath}\"") { UseShellExecute = true });
    }

    private void CopyPathButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        Clipboard.SetText(selected.PreferredCopy.FilePath);
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

        Process.Start(new ProcessStartInfo("pwsh.exe", $"-NoExit -Command \"{command}\"") { UseShellExecute = true });
        StatusTextBlock.Text = $"Opened Codex resume command for {selected.SessionId}.";
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSession();
        if (selected is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"{selected.SessionId}.md",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt|JSON (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, ReadableTranscriptTextBox.Text, Encoding.UTF8);
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

    private async void ExecuteMaintenanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentMaintenancePreview is null || _maintenanceExecutor is null)
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
            var result = await _maintenanceExecutor.ExecuteAsync(_currentMaintenancePreview, destinationRoot, TypedConfirmationTextBox.Text, CancellationToken.None);
            StatusTextBlock.Text = result.Executed
                ? $"Executed maintenance. Checkpoint: {result.ManifestPath}"
                : "Maintenance did not execute.";
            await RefreshAsync(deepScan: false);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Maintenance failed: {ex.Message}";
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
            .Where(File.Exists)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Path.GetFileName(path)} | {Math.Round(info.Length / 1024.0 / 1024.0, 1)} MB | {info.LastWriteTime}";
            })
            .ToArray();

        return details.Length == 0
            ? "No live SQLite store detected."
            : string.Join(Environment.NewLine, details);
    }
}
