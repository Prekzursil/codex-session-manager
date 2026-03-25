using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexSessionManager.App;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;
using Microsoft.Win32;
using Xunit;

namespace CodexSessionManager.App.Tests;

public sealed class MainWindowCoverageTests
{
    private static readonly MethodInfo BuildKnownStoresMethod =
        typeof(MainWindow).GetMethod("BuildKnownStores", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo GetLiveSqliteStatusMethod =
        typeof(MainWindow).GetMethod("GetLiveSqliteStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo InitializeAsyncMethod =
        typeof(MainWindow).GetMethod("InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LoadSessionsFromCatalogAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSessionsFromCatalogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RefreshAsyncMethod =
        typeof(MainWindow).GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo HandleSelectionChangedAsyncMethod =
        typeof(MainWindow).GetMethod("HandleSessionSelectionChangedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo HandleSearchTextChangedAsyncMethod =
        typeof(MainWindow).GetMethod("HandleSearchTextChangedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SaveMetadataAsyncCoreMethod =
        typeof(MainWindow).GetMethod("SaveMetadataAsyncCore", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExecuteMaintenanceAsyncCoreMethod =
        typeof(MainWindow).GetMethod("ExecuteMaintenanceAsyncCore", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo OpenFolderMethod =
        typeof(MainWindow).GetMethod("OpenFolderButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo OpenRawMethod =
        typeof(MainWindow).GetMethod("OpenRawButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo CopyPathMethod =
        typeof(MainWindow).GetMethod("CopyPathButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ResumeMethod =
        typeof(MainWindow).GetMethod("ResumeButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExportMethod =
        typeof(MainWindow).GetMethod("ExportButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo BuildPreviewMethod =
        typeof(MainWindow).GetMethod("BuildPreviewButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void Constructor_initializes_core_bindings() =>
        RunInSta(() =>
        {
            var window = new MainWindow();

            Assert.NotNull(FindRequired<ListBox>(window, "SessionsListBox").ItemsSource);
            Assert.Equal(MaintenanceAction.Archive, FindRequired<ComboBox>(window, "MaintenanceActionComboBox").SelectedItem);
            Assert.Equal("Starting…", FindRequired<TextBlock>(window, "StatusTextBlock").Text);

        });

    [Fact]
    public void BuildKnownStores_matches_locator_for_non_deep_scan()
    {
        var expected = CodexSessionManager.Storage.Discovery.KnownStoreLocator.GetKnownStores(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));

        var actual = (IReadOnlyList<CodexSessionManager.Storage.Discovery.KnownSessionStore>)BuildKnownStoresMethod.Invoke(null, [false])!;

        Assert.Equal(expected.Select(store => store.SessionsPath), actual.Select(store => store.SessionsPath));
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_non_empty_status()
    {
        var value = (string)GetLiveSqliteStatusMethod.Invoke(null, [])!;
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public void InitializeAsync_uses_temp_root_and_loads_cached_sessions() =>
        RunInSta(() =>
        {
            var root = CreateTemporaryDirectory();
            var window = new MainWindow();
            var backgroundRefreshQueued = false;
            SetPrivateField(window, "_localDataRootOverride", (Func<string>)(() => root));
            SetPrivateField(window, "_queueInitialRefreshOverride", (Action)(() => backgroundRefreshQueued = true));
            SetImmediateUiDispatch(window);

            InvokePrivateTask(window, InitializeAsyncMethod).GetAwaiter().GetResult();

            Assert.Equal(Path.Combine(root, "maintenance", "archive"), FindRequired<TextBox>(window, "DestinationRootTextBox").Text);
            Assert.Equal("Loaded 0 sessions from cached index.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);
            Assert.True(backgroundRefreshQueued);
        });

    [Fact]
    public void HandleSessionSelectionChangedAsync_populates_details_using_seams() =>
        RunInSta(() =>
        {
            var window = new MainWindow();
            var session = BuildSession("session-1", "Renderer work", "Readable transcript A");
            AddSession(window, session);
            SetImmediateUiDispatch(window);
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = session;

            SetPrivateField(window, "_liveSqliteStatusOverride", (Func<string>)(() => "sqlite-ok"));
            SetPrivateField(window, "_readRawFileTextOverride", (Func<string, string>)(_ => "raw body"));
            SetPrivateField(
                window,
                "_parseSessionFileAsyncOverride",
                (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(BuildParsedSessionFile(session.SessionId, @"C:\repo"))));
            SetPrivateField(
                window,
                "_renderTranscriptOverride",
                (Func<NormalizedSessionDocument, TranscriptMode, TranscriptRenderResult>)((document, mode) => new TranscriptRenderResult(mode, $"{mode}:{document.SessionId}")));

            InvokePrivateTask(window, HandleSelectionChangedAsyncMethod).GetAwaiter().GetResult();

            Assert.Equal("Renderer work", FindRequired<TextBlock>(window, "ThreadNameTextBlock").Text);
            Assert.Equal(session.SessionId, FindRequired<TextBlock>(window, "SessionIdTextBlock").Text);
            Assert.Equal(session.PreferredCopy.FilePath, FindRequired<TextBlock>(window, "PreferredPathTextBlock").Text);
            Assert.Equal(@"C:\repo", FindRequired<TextBlock>(window, "CwdTextBlock").Text);
            Assert.Equal("sqlite-ok", FindRequired<TextBlock>(window, "SQLiteStatusTextBlock").Text);
            Assert.Equal("raw body", FindRequired<TextBox>(window, "RawTranscriptTextBox").Text);
            Assert.Equal($"Readable:{session.SessionId}", FindRequired<TextBox>(window, "ReadableTranscriptTextBox").Text);
            Assert.Equal($"Dialogue:{session.SessionId}", FindRequired<TextBox>(window, "DialogueTranscriptTextBox").Text);
            Assert.Equal($"Audit:{session.SessionId}", FindRequired<TextBox>(window, "AuditTranscriptTextBox").Text);
        });

    [Fact]
    public void HandleSessionSelectionChangedAsync_shows_error_state_when_parser_fails() =>
        RunInSta(() =>
        {
            var window = new MainWindow();
            var session = BuildSession("session-2", "Broken transcript", "Readable transcript");
            AddSession(window, session);
            SetImmediateUiDispatch(window);
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = session;
            SetPrivateField(
                window,
                "_parseSessionFileAsyncOverride",
                (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => throw new InvalidOperationException("parser failed")));

            InvokePrivateTask(window, HandleSelectionChangedAsyncMethod).GetAwaiter().GetResult();

            Assert.Equal("-", FindRequired<TextBlock>(window, "CwdTextBlock").Text);
            Assert.Equal("Live SQLite status unavailable.", FindRequired<TextBlock>(window, "SQLiteStatusTextBlock").Text);
            Assert.Equal(string.Empty, FindRequired<TextBox>(window, "AuditTranscriptTextBox").Text);
            Assert.Contains("parser failed", FindRequired<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);
        });

    [Fact]
    public void HandleSearchTextChangedAsync_filters_and_resets_sessions() =>
        RunInSta(() =>
        {
            var root = CreateTemporaryDirectory();
            var repository = CreateRepositoryAsync(root, BuildSession("session-1", "Renderer work", "alpha"), BuildSession("session-2", "Maintenance", "beta")).GetAwaiter().GetResult();
            var window = new MainWindow();
            SetPrivateField(window, "_repository", repository);
            SetImmediateUiDispatch(window);

            InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod).GetAwaiter().GetResult();
            Assert.Equal(2, FindRequired<ListBox>(window, "SessionsListBox").Items.Count);

            var searchBox = FindRequired<TextBox>(window, "SearchTextBox");
            DetachTextChangedHandler(window, searchBox);
            searchBox.Text = "beta";
            InvokePrivateTask(window, HandleSearchTextChangedAsyncMethod).GetAwaiter().GetResult();

            Assert.Equal(1, FindRequired<ListBox>(window, "SessionsListBox").Items.Count);
            Assert.Equal("Search returned 1 sessions.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);

            searchBox.Text = string.Empty;
            InvokePrivateTask(window, HandleSearchTextChangedAsyncMethod).GetAwaiter().GetResult();

            Assert.Equal(2, FindRequired<ListBox>(window, "SessionsListBox").Items.Count);
            Assert.Equal("Loaded 2 sessions from cached index.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);
        });

    [Fact]
    public void SaveMetadataAsyncCore_persists_metadata_for_selected_session() =>
        RunInSta(() =>
        {
            var root = CreateTemporaryDirectory();
            var session = BuildSession("session-3", "Metadata", "gamma");
            var repository = CreateRepositoryAsync(root, session).GetAwaiter().GetResult();
            var window = new MainWindow();
            SetPrivateField(window, "_repository", repository);
            SetImmediateUiDispatch(window);

            InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod).GetAwaiter().GetResult();
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = listBox.Items[0];
            FindRequired<TextBox>(window, "AliasTextBox").Text = "Alias";
            FindRequired<TextBox>(window, "TagsTextBox").Text = "one, two";
            FindRequired<TextBox>(window, "NotesTextBox").Text = "saved note";

            InvokePrivateTask(window, SaveMetadataAsyncCoreMethod).GetAwaiter().GetResult();

            var stored = repository.ListSessionsAsync(CancellationToken.None).GetAwaiter().GetResult().Single();
            Assert.Equal("Alias", stored.SearchDocument.Alias);
            Assert.Equal(["one", "two"], stored.SearchDocument.Tags);
            Assert.Equal("saved note", stored.SearchDocument.Notes);
            Assert.Equal($"Saved metadata for {stored.SessionId}.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);
        });

    [Fact]
    public void ProcessAndClipboardHandlers_use_overrides_for_external_actions() =>
        RunInSta(() =>
        {
            var window = new MainWindow();
            var startedProcesses = new List<ProcessStartInfo>();
            string? copiedText = null;
            var session = BuildSession("session-4", "Launchers", "delta");
            AddSession(window, session);
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = session;
            FindRequired<TextBlock>(window, "CwdTextBlock").Text = "-";
            SetPrivateField(window, "_launchProcessOverride", (Func<ProcessStartInfo, Process?>)(info =>
            {
                startedProcesses.Add(info);
                return null;
            }));
            SetPrivateField(window, "_setClipboardTextOverride", (Action<string>)(value => copiedText = value));

            OpenFolderMethod.Invoke(window, [window, new RoutedEventArgs()]);
            OpenRawMethod.Invoke(window, [window, new RoutedEventArgs()]);
            CopyPathMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);

            Assert.Equal(3, startedProcesses.Count);
            Assert.Equal("explorer.exe", startedProcesses[0].FileName);
            Assert.Equal("notepad.exe", startedProcesses[1].FileName);
            Assert.Equal("pwsh.exe", startedProcesses[2].FileName);
            Assert.Contains("codex resume session-4", startedProcesses[2].Arguments, StringComparison.Ordinal);
            Assert.Equal(session.PreferredCopy.FilePath, copiedText);
            Assert.Equal("Opened Codex resume command for session-4.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);

        });

    [Fact]
    public void ExportButton_writes_transcript_when_dialog_is_accepted() =>
        RunInSta(() =>
        {
            var window = new MainWindow();
            string? writtenPath = null;
            string? writtenContent = null;
            Encoding? writtenEncoding = null;
            var session = BuildSession("session-5", "Export", "epsilon");
            AddSession(window, session);
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = session;
            FindRequired<TextBox>(window, "ReadableTranscriptTextBox").Text = "export me";
            SetPrivateField(window, "_showSaveFileDialogOverride", (Func<SaveFileDialog, bool?>)(dialog =>
            {
                dialog.FileName = @"C:\tmp\session-5.md";
                return true;
            }));
            SetPrivateField(window, "_writeTextFileOverride", (Action<string, string, Encoding>)((path, content, encoding) =>
            {
                writtenPath = path;
                writtenContent = content;
                writtenEncoding = encoding;
            }));

            ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);

            Assert.Equal(@"C:\tmp\session-5.md", writtenPath);
            Assert.Equal("export me", writtenContent);
            Assert.Equal(Encoding.UTF8.WebName, writtenEncoding!.WebName);
            Assert.Equal("Exported session to C:\\tmp\\session-5.md.", FindRequired<TextBlock>(window, "StatusTextBlock").Text);

        });

    [Fact]
    public void BuildPreview_and_ExecuteMaintenance_cover_success_and_failure_paths() =>
        RunInSta(() =>
        {
            var session = BuildSession("session-6", "Maintenance", "zeta") with
            {
                PreferredCopy = new SessionPhysicalCopy("session-6", @"C:\tmp\session-6.jsonl", SessionStoreKind.Backup, DateTimeOffset.UtcNow, 128, false),
                PhysicalCopies =
                [
                    new SessionPhysicalCopy("session-6", @"C:\tmp\session-6.jsonl", SessionStoreKind.Backup, DateTimeOffset.UtcNow, 128, false)
                ]
            };

            var window = new MainWindow();
            AddSession(window, session);
            var listBox = FindRequired<ListBox>(window, "SessionsListBox");
            DetachSelectionChangedHandler(window, listBox);
            listBox.SelectedItem = session;
            SetImmediateUiDispatch(window);
            var executionCount = 0;
            SetPrivateField(
                window,
                "_executeMaintenancePreviewAsyncOverride",
                (Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((preview, destinationRoot, typedConfirmation, _) =>
                {
                    executionCount++;
                    if (executionCount == 1)
                    {
                        return Task.FromResult(
                            new MaintenanceExecutionResult(
                                true,
                                preview.AllowedTargets,
                                Path.Combine(destinationRoot, "manifest.json")));
                    }

                    throw new InvalidOperationException("forced failure");
                }));

            BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);

            Assert.Contains("Allowed: 1", FindRequired<TextBlock>(window, "MaintenanceSummaryTextBlock").Text, StringComparison.Ordinal);
            Assert.Contains("Dangerous maintenance target", FindRequired<TextBox>(window, "MaintenanceWarningsTextBox").Text, StringComparison.Ordinal);

            FindRequired<TextBox>(window, "DestinationRootTextBox").Text = @"C:\tmp\archive";
            InvokePrivateTask(window, ExecuteMaintenanceAsyncCoreMethod).GetAwaiter().GetResult();

            Assert.Contains("Executed maintenance. Checkpoint:", FindRequired<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);

            BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
            FindRequired<TextBox>(window, "TypedConfirmationTextBox").Text = "wrong";
            InvokePrivateTask(window, ExecuteMaintenanceAsyncCoreMethod).GetAwaiter().GetResult();

            Assert.Contains("Maintenance failed:", FindRequired<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
        });

    private static async Task<SessionCatalogRepository> CreateRepositoryAsync(string root, params IndexedLogicalSession[] sessions)
    {
        var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));
        await repository.InitializeAsync(CancellationToken.None);
        foreach (var session in sessions)
        {
            await repository.UpsertAsync(session, CancellationToken.None);
        }

        return repository;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "csm-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTemporaryFile(string fileName, string content)
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static void AddSession(MainWindow window, IndexedLogicalSession session)
    {
        var sessions = (System.Collections.ObjectModel.ObservableCollection<IndexedLogicalSession>)
            typeof(MainWindow).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        sessions.Add(session);
    }

    private static NormalizedSessionDocument BuildDocument(string sessionId) =>
        new(
            sessionId,
            "Thread title",
            DateTimeOffset.UtcNow,
            null,
            @"C:\repo",
            [NormalizedSessionEvent.CreateMessage(SessionActor.User, "hello")]);

    private static ParsedSessionFile BuildParsedSessionFile(string sessionId, string cwd) =>
        new(
            sessionId,
            null,
            cwd,
            new TechnicalBreadcrumbs([], [], [], []),
            BuildDocument(sessionId));

    private static IndexedLogicalSession BuildSession(string sessionId, string threadName, string transcript) =>
        new(
            sessionId,
            threadName,
            new SessionPhysicalCopy(
                sessionId,
                Path.Combine(Path.GetTempPath(), $"{sessionId}.jsonl"),
                SessionStoreKind.Backup,
                DateTimeOffset.UtcNow,
                1024,
                false),
            [
                new SessionPhysicalCopy(
                    sessionId,
                    Path.Combine(Path.GetTempPath(), $"{sessionId}.jsonl"),
                    SessionStoreKind.Backup,
                    DateTimeOffset.UtcNow,
                    1024,
                    false)
            ],
            new SessionSearchDocument(
                transcript,
                transcript,
                "tool summary",
                "rg command",
                [],
                [],
                string.Empty,
                string.Empty,
                [],
                string.Empty));

    private static T FindRequired<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"Element '{name}' was not found.");

    private static async Task InvokePrivateTask(object instance, MethodInfo method, params object?[] args)
    {
        var task = (Task?)method.Invoke(instance, args);
        if (task is not null)
        {
            await task;
        }
    }

    private static void SetPrivateField(object instance, string fieldName, object? value) =>
        typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void SetImmediateUiDispatch(MainWindow window) =>
        SetPrivateField(window, "_invokeOnUiAsyncOverride", (Func<Action, Task>)(action =>
        {
            if (window.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                window.Dispatcher.Invoke(action);
            }

            return Task.CompletedTask;
        }));

    private static void DetachSelectionChangedHandler(MainWindow window, ListBox listBox)
    {
        var handler = (SelectionChangedEventHandler)Delegate.CreateDelegate(
            typeof(SelectionChangedEventHandler),
            window,
            "SessionsListBox_OnSelectionChanged");
        listBox.SelectionChanged -= handler;
    }

    private static void DetachTextChangedHandler(MainWindow window, TextBox textBox)
    {
        var handler = (TextChangedEventHandler)Delegate.CreateDelegate(
            typeof(TextChangedEventHandler),
            window,
            "SearchTextBox_OnTextChanged");
        textBox.TextChanged -= handler;
    }

    private static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new TargetInvocationException(error);
        }
    }

}
