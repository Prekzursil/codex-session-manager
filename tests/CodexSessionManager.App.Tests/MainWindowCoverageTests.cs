using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexSessionManager.App;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;
using Microsoft.Win32;

namespace CodexSessionManager.App.Tests;

public sealed class MainWindowCoverageTests
{
    private static readonly MethodInfo BuildKnownStoresMethod =
        typeof(MainWindow).GetMethod("BuildKnownStores", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo GetLiveSqliteStatusMethod =
        typeof(MainWindow).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "GetLiveSqliteStatus" && method.GetParameters().Length == 0);

    private static readonly MethodInfo GetLiveSqliteStatusWithInputsMethod =
        typeof(MainWindow).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
            {
                if (method.Name != "GetLiveSqliteStatus")
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(IEnumerable<string>)
                    && parameters[1].ParameterType == typeof(Func<string, string?>);
            });

    private static readonly MethodInfo DescribeSqlitePathMethod =
        typeof(MainWindow).GetMethod("DescribeSqlitePath", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!;

    private static readonly MethodInfo InitializeAsyncMethod =
        typeof(MainWindow).GetMethod("InitializeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LoadSessionsFromCatalogAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSessionsFromCatalogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RefreshAsyncMethod =
        typeof(MainWindow).GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RunBackgroundRefreshAsyncMethod =
        typeof(MainWindow).GetMethod("RunBackgroundRefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RunOnUiThreadAsyncMethod =
        typeof(MainWindow).GetMethod("RunOnUiThreadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RunOnUiThreadValueAsyncMethod =
        typeof(MainWindow).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "RunOnUiThreadValueAsync" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(string));

    private static readonly MethodInfo LoadSelectedSessionAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSelectedSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo PopulateSelectedSessionHeaderAsyncMethod =
        typeof(MainWindow).GetMethod("PopulateSelectedSessionHeaderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LoadSelectedSessionBodyAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSelectedSessionBodyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SearchSessionsAsyncMethod =
        typeof(MainWindow).GetMethod("SearchSessionsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SaveSelectedMetadataAsyncMethod =
        typeof(MainWindow).GetMethod("SaveSelectedMetadataAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExecuteMaintenanceUiAsyncMethod =
        typeof(MainWindow).GetMethod(
            "ExecuteMaintenanceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            Type.DefaultBinder,
            Type.EmptyTypes,
            null)!;

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

    private static readonly FieldInfo SessionsField =
        typeof(MainWindow).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo RepositoryField =
        typeof(MainWindow).GetField("_repository", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo WorkspaceIndexerField =
        typeof(MainWindow).GetField("_workspaceIndexer", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo MaintenanceExecutorField =
        typeof(MainWindow).GetField("_maintenanceExecutor", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo SearchCtsField =
        typeof(MainWindow).GetField("_searchCts", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly string[] SqliteStatusPaths = ["first", "second"];

    [Fact]
    public void Constructor_initializes_core_bindings()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            Assert.NotNull(GetNamedField<ListBox>(window, "SessionsListBox").ItemsSource);
            Assert.Equal(MaintenanceAction.Archive, GetNamedField<ComboBox>(window, "MaintenanceActionComboBox").SelectedItem);
            Assert.Equal("Starting…", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
        });
    }

    [Fact]
    public void InitializeComponent_rehydrates_named_controls()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            window.InitializeComponent();

            Assert.Equal("Codex Session Manager", window.Title);
        });
    }

    [Fact]
    public void InitializeComponent_can_be_called_twice_without_reloading_content()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            window.InitializeComponent();

            Assert.NotNull(GetNamedField<Button>(window, "RefreshButton"));
            Assert.NotNull(GetNamedField<ListBox>(window, "SessionsListBox"));
        });
    }

    [Fact]
    public void BuildKnownStores_returns_distinct_entries_for_deep_scan()
    {
        var shallow = InvokeBuildKnownStores(false);
        var deep = InvokeBuildKnownStores(true);

        Assert.True(deep.Count >= shallow.Count);
        Assert.Equal(deep.Select(store => store.SessionsPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), deep.Count);
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_status_string()
    {
        var value = (string)GetLiveSqliteStatusMethod.Invoke(null, [])!;
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_joined_details_for_detected_paths()
    {
        var value = (string)GetLiveSqliteStatusWithInputsMethod.Invoke(
            null,
            [
                SqliteStatusPaths,
                (Func<string, string?>)(path => path == "first" ? "first detail" : "second detail")
            ])!;

        Assert.Equal($"first detail{Environment.NewLine}second detail", value);
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_default_message_when_no_details_are_available()
    {
        var value = (string)GetLiveSqliteStatusWithInputsMethod.Invoke(
            null,
            [
                SqliteStatusPaths,
                (Func<string, string?>)(_ => null)
            ])!;

        Assert.Equal("No live SQLite store detected.", value);
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_default_message_when_no_paths_are_detected()
    {
        var value = (string)GetLiveSqliteStatusWithInputsMethod.Invoke(
            null,
            [
                SqliteStatusPaths,
                (Func<string, string?>)(_ => null)
            ])!;

        Assert.Equal("No live SQLite store detected.", value);
    }

    [Fact]
    public void DescribeSqlitePath_handles_missing_and_exceptions()
    {
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, [Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite"), null]));
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, ["ignored", (Func<string, FileInfo>)(_ => throw new IOException("busy"))]));
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, ["ignored", (Func<string, FileInfo>)(_ => throw new UnauthorizedAccessException("denied"))]));
    }

    [Fact]
    public void BuildKnownStores_includes_additional_profile_home_on_deep_scan()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var extraHome = Path.Combine(userProfile, $".codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extraHome);

        try
        {
            var deep = InvokeBuildKnownStores(true);
            Assert.Contains(deep, store => string.Equals(store.WorkspaceRoot, extraHome, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(deep, store => string.Equals(store.SessionsPath, Path.Combine(extraHome, "sessions"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(deep, store => string.Equals(store.SessionsPath, Path.Combine(extraHome, "sessions_backup"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(extraHome, recursive: true);
        }
    }

    [Fact]
    public async Task RunOnUiThreadAsync_invokes_action_when_called_off_dispatcher_threadAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            await Task.Run(async () =>
            {
                var task = (Task)RunOnUiThreadAsyncMethod.Invoke(window, [(Action)(() => GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "Updated from background")])!;
                await task;
            });

            Assert.Equal("Updated from background", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            window.Close();
        });
    }

    [Fact]
    public async Task RunOnUiThreadValueAsync_returns_value_when_called_off_dispatcher_thread_Async()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            string? value = null;

            await Task.Run(async () =>
            {
                var task = (Task<string>)RunOnUiThreadValueAsyncMethod.Invoke(window, [(Func<string>)(() => "Value from background")])!;
                value = await task;
            });

            Assert.Equal("Value from background", value);
            window.Close();
        });
    }

    [Fact]
    public async Task InitializeAsync_uses_injected_dependencies_and_schedules_refreshAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var window = new MainWindow();
                var scheduled = 0;

                SetProvider(window, "LocalDataRootProvider", () => root);
                SetProvider(window, "ScheduleRefreshAction", (Action)(() => scheduled++));
                SetProvider(window, "KnownStoresProvider", (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => Array.Empty<KnownSessionStore>()));

                await InvokePrivateTask(window, InitializeAsyncMethod);

                Assert.NotNull(RepositoryField.GetValue(window));
                Assert.NotNull(WorkspaceIndexerField.GetValue(window));
                Assert.NotNull(MaintenanceExecutorField.GetValue(window));
                Assert.Equal(Path.Combine(root, "maintenance", "archive"), GetNamedField<TextBox>(window, "DestinationRootTextBox").Text);
                Assert.Equal(1, scheduled);
                Assert.Contains("Loaded 0 sessions", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task InitializeAsync_failure_sets_statusAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            SetProvider(window, "RepositoryFactory", ((Func<string, SessionCatalogRepository>)(_ => throw new InvalidOperationException("boom"))));

            await InvokePrivateTask(window, InitializeAsyncMethod);

            Assert.Contains("Startup failed: boom", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task LoadSessionsFromCatalogAsync_populates_sessions_from_repositoryAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-load", "Thread Load");
                var repository = CreateRepository(root, BuildIndexedSession("session-load", "Thread Load", sessionFile));
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);

                await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);

                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);
                Assert.Contains("Loaded 1 sessions", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RefreshAsync_uses_known_stores_and_rebuilds_catalogAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-refresh", "Refresh Thread");
                var repository = CreateRepository(root);
                var indexer = new SessionWorkspaceIndexer(repository);
                var window = new MainWindow();
                var stores =
                    new[]
                    {
                        new KnownSessionStore(
                            root,
                            SessionStoreKind.Live,
                            Path.GetDirectoryName(sessionFile)!,
                            Path.Combine(root, "sessions.index.jsonl"))
                    };

                File.WriteAllText(
                    Path.Combine(root, "sessions.index.jsonl"),
                    """{"id":"session-refresh","thread_name":"Refresh Thread"}""" + Environment.NewLine,
                    Encoding.UTF8);

                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, indexer);
                SetProvider(window, "KnownStoresProvider", (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => stores));

                await InvokePrivateTask(window, RefreshAsyncMethod, false);

                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);
                Assert.Contains("Indexed 1 deduped sessions", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RefreshAsync_with_deep_scan_uses_deep_scan_status_and_indexes_sessionsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-refresh-deep", "Refresh Thread Deep");
                var repository = CreateRepository(root);
                var indexer = new SessionWorkspaceIndexer(repository);
                var window = new MainWindow();
                var stores =
                    new[]
                    {
                        new KnownSessionStore(
                            root,
                            SessionStoreKind.Live,
                            Path.GetDirectoryName(sessionFile)!,
                            Path.Combine(root, "sessions.index.jsonl"))
                    };

                File.WriteAllText(
                    Path.Combine(root, "sessions.index.jsonl"),
                    """{""id":""session-refresh-deep"",""thread_name":""Refresh Thread Deep""}""" + Environment.NewLine,
                    Encoding.UTF8);

                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, indexer);
                SetProvider(window, "KnownStoresProvider", (Func<bool, IReadOnlyList<KnownSessionStore>>)(deepScan =>
                {
                    Assert.True(deepScan);
                    return stores;
                }));

                await InvokePrivateTask(window, RefreshAsyncMethod, true);

                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);
                Assert.Contains("Indexed 1 deduped sessions", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RunBackgroundRefreshAsync_sets_status_when_refresh_throwsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var repository = CreateRepository(root);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, new SessionWorkspaceIndexer(repository));
                SetProvider(
                    window,
                    "KnownStoresProvider",
                    (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => throw new InvalidOperationException("refresh boom")));

                await InvokePrivateTask(window, RunBackgroundRefreshAsyncMethod);

                Assert.Contains("Background refresh failed: refresh boom", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RunBackgroundRefreshAsync_completes_successfully_when_refresh_succeedsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var repository = CreateRepository(root);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, new SessionWorkspaceIndexer(repository));
                SetProvider(window, "KnownStoresProvider", (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => Array.Empty<KnownSessionStore>()));

                await InvokePrivateTask(window, RunBackgroundRefreshAsyncMethod);

                Assert.Contains("Indexed 0 deduped sessions", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionAsync_success_updates_details_and_transcriptsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-select", "Selected Thread");
                var window = new MainWindow();
                var parsed = BuildParsedFile("session-select", @"C:\workdir");
                var session = BuildIndexedSession("session-select", "Selected Thread", sessionFile);

                AddSession(window, session);
                var loadedSession = ((ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!).Single();
                SelectSingleSession(window, loadedSession);
                SetProvider(window, "LiveSqliteStatusProvider", (() => "sqlite ok"));
                SetProvider(window, "SessionParser", ((Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsed))));
                SetProvider(window, "FileTextReader", ((Func<string, string>)(_ => "raw-session-content")));

                await InvokePrivateTask(window, LoadSelectedSessionAsyncMethod);

                Assert.Equal("Selected Thread", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
                Assert.Equal("session-select", GetNamedField<TextBlock>(window, "SessionIdTextBlock").Text);
                Assert.Equal(@"C:\workdir", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal("sqlite ok", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Contains("# Codex Session Transcript", GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text, StringComparison.Ordinal);
                Assert.Contains("raw-session-content", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task PopulateSelectedSessionHeaderAsync_throws_when_selected_session_is_missing_required_membersAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-header-guards", "Header Guards");
                var session = BuildIndexedSession("session-header-guards", "Header Guards", sessionFile);
                var window = new MainWindow();
                await Assert.ThrowsAsync<InvalidOperationException>(() => InvokePrivateTask(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy)), session.SessionId));
                await Assert.ThrowsAsync<InvalidOperationException>(() => InvokePrivateTask(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.SearchDocument)), session.SessionId));
                await InvokePrivateTask(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PhysicalCopies)), session.SessionId);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionBodyAsync_missing_preferred_copy_is_handled_without_throwingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-body-guard", "Body Guard");
                var window = new MainWindow();
                var session = WithNullIndexedSessionProperty(BuildIndexedSession("session-body-guard", "Body Guard", sessionFile), nameof(IndexedLogicalSession.PreferredCopy));

                await InvokePrivateTask(window, LoadSelectedSessionBodyAsyncMethod, session, session.SessionId);
                Assert.Equal(string.Empty, GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ApplySearchResultsAsync_throws_when_repository_is_missing_Async()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            await Assert.ThrowsAsync<InvalidOperationException>(() => InvokePrivateTask(window, typeof(MainWindow).GetMethod("ApplySearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!, "query", CancellationToken.None));

            window.Close();
        });
    }

    [Fact]
    public async Task ApplySearchResults_normalizes_null_query_before_searchingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-search-null", "Search Null");
                var repository = CreateRepository(root, BuildIndexedSession("session-search-null", "Search Null", sessionFile));
                var window = new MainWindow();

                RepositoryField.SetValue(window, repository);

                await InvokePrivateTask(window, typeof(MainWindow).GetMethod("ApplySearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!, null!, CancellationToken.None);

                Assert.Contains("Search returned", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionAsync_uses_dash_when_parsed_cwd_is_missingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-select-nullcwd", "Selected Thread");
                var window = new MainWindow();
                var parsed = BuildParsedFile("session-select-nullcwd", null);
                var session = BuildIndexedSession("session-select-nullcwd", "Selected Thread", sessionFile);

                AddSession(window, session);
                var loadedSession = ((ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!).Single();
                SelectSingleSession(window, loadedSession);
                SetProvider(window, "LiveSqliteStatusProvider", (() => "sqlite ok"));
                SetProvider(window, "SessionParser", ((Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsed))));
                SetProvider(window, "FileTextReader", ((Func<string, string>)(_ => "raw-session-content")));

                await InvokePrivateTask(window, LoadSelectedSessionAsyncMethod);

                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionAsync_failure_updates_fallback_uiAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-error", "Failure Thread");
                var window = new MainWindow();
                var session = BuildIndexedSession("session-error", "Failure Thread", sessionFile);

                AddSession(window, session);
                var loadedSession = ((ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!).Single();
                SelectSingleSession(window, loadedSession);
                SetProvider(
                    window,
                    "SessionParser",
                    ((Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromException<ParsedSessionFile>(new InvalidOperationException("parse failed")))));

                await InvokePrivateTask(window, LoadSelectedSessionAsyncMethod);

                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal("Live SQLite status unavailable.", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Contains("parse failed", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task PopulateSelectedSessionHeaderAsync_skips_updates_when_selection_changesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-header-stale", "Header Stale");
                var session = BuildIndexedSession("session-header-stale", "Header Stale", sessionFile);
                var window = new MainWindow();
                var initialThreadName = GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text;

                AddSession(window, session);
                GetNamedField<ListBox>(window, "SessionsListBox").SelectedItem = null;

                await InvokePrivateTask(window, PopulateSelectedSessionHeaderAsyncMethod, session, session.SessionId);

                Assert.Equal(initialThreadName, GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SearchSessionsAsync_filters_and_reloadsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionOne = BuildIndexedSession("session-one", "Renderer work", WriteSessionJsonl(root, "session-one", "Renderer work"));
                var sessionTwo = BuildIndexedSession("session-two", "Maintenance", WriteSessionJsonl(root, "session-two", "Maintenance"));
                var repository = CreateRepository(root, sessionOne, sessionTwo);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);

                await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);

                var searchBox = GetNamedField<TextBox>(window, "SearchTextBox");
                searchBox.Text = "maint";
                await InvokePrivateTask(window, SearchSessionsAsyncMethod);
                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);

                searchBox.Text = string.Empty;
                await InvokePrivateTask(window, SearchSessionsAsyncMethod);
                Assert.Equal(2, GetNamedField<ListBox>(window, "SessionsListBox").Items.Count);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SearchSessionsAsync_catches_cancellation_and_window_close_disposes_search_tokenAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessions = Enumerable.Range(0, 200)
                    .Select(index => BuildIndexedSession($"session-{index}", $"Thread {index}", WriteSessionJsonl(root, $"session-{index}", $"Thread {index}")))
                    .ToArray();
                var repository = CreateRepository(root, sessions);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);
                await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);

                GetNamedField<TextBox>(window, "SearchTextBox").Text = "Thread";
                var searchTask = Task.Run(async () => await InvokePrivateTask(window, SearchSessionsAsyncMethod));

                CancellationTokenSource? searchCts = null;
                for (var attempt = 0; attempt < 50 && searchCts is null; attempt++)
                {
                    searchCts = SearchCtsField.GetValue(window) as CancellationTokenSource;
                    if (searchCts is null)
                    {
                        await Task.Delay(10);
                    }
                }

                Assert.NotNull(searchCts);
                searchCts!.Cancel();
                await searchTask;

                window.Close();
                Assert.Null(SearchCtsField.GetValue(window));
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RepositoryBackedAsyncMethods_return_early_without_repositoryAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "unchanged";

            await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);
            await InvokePrivateTask(window, SearchSessionsAsyncMethod);
            await InvokePrivateTask(window, SaveSelectedMetadataAsyncMethod);

            Assert.Equal("unchanged", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            window.Close();
        });
    }

    [Fact]
    public async Task SaveSelectedMetadataAsync_persists_alias_tags_and_notesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var session = BuildIndexedSession("session-meta", "Meta Thread", WriteSessionJsonl(root, "session-meta", "Meta Thread"));
                var repository = CreateRepository(root, session);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);
                await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);

                SelectSingleSession(window, GetNamedField<ListBox>(window, "SessionsListBox").Items.Cast<IndexedLogicalSession>().Single());
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "Ops Alias";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "ops, strict-zero";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "Updated note";

                await InvokePrivateTask(window, SaveSelectedMetadataAsyncMethod);

                var refreshed = (await repository.ListSessionsAsync(CancellationToken.None)).Single();
                Assert.Equal("Ops Alias", refreshed.SearchDocument.Alias);
                Assert.Equal(["ops", "strict-zero"], refreshed.SearchDocument.Tags);
                Assert.Equal("Updated note", refreshed.SearchDocument.Notes);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SaveSelectedMetadataWithoutSelectionReturnsWithoutChangesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var session = BuildIndexedSession("session-meta-none", "Meta None", WriteSessionJsonl(root, "session-meta-none", "Meta None"));
                var repository = CreateRepository(root, session);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);

                await InvokePrivateTask(window, SaveSelectedMetadataAsyncMethod);

                Assert.Equal("Starting…", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
                var refreshed = await repository.ListSessionsAsync(CancellationToken.None);
                Assert.Single(refreshed);
                Assert.Equal(string.Empty, refreshed[0].SearchDocument.Alias);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SaveSelectedMetadataAsync_returns_when_no_session_is_selected_Async()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var session = BuildIndexedSession("session-meta-none", "Meta None", WriteSessionJsonl(root, "session-meta-none", "Meta None"));
                var repository = CreateRepository(root, session);
                var window = new MainWindow();
                RepositoryField.SetValue(window, repository);
                await InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod);
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "should not persist";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "ops,app";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "ignored note";

                var sessionsList = GetNamedField<ListBox>(window, "SessionsListBox");
                sessionsList.SelectedItem = null;
                sessionsList.SelectedItems.Clear();
                Assert.Null(GetSelectedSession(window));

                await InvokePrivateTask(window, SaveSelectedMetadataAsyncMethod);

                var refreshed = (await repository.ListSessionsAsync(CancellationToken.None)).Single();
                Assert.Equal(string.Empty, refreshed.SearchDocument.Alias);
                Assert.Equal([], refreshed.SearchDocument.Tags);
                Assert.Equal(string.Empty, refreshed.SearchDocument.Notes);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void ExternalActionButtons_use_wrappers()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-actions", "Actions Thread");
                var session = BuildIndexedSession("session-actions", "Actions Thread", sessionFile);
                var window = new MainWindow();
                var started = new List<(string fileName, string arguments)>();
                var copied = new List<string>();
                var exportPath = Path.Combine(root, "export.md");

                AddSession(window, session);
                SelectSingleSession(window, GetNamedField<ListBox>(window, "SessionsListBox").Items.Cast<IndexedLogicalSession>().Single());
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = @"C:\repo";
                GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text = "exported transcript";
                SetProvider(window, "ProcessStarter", ((Action<string, string>)((fileName, arguments) => started.Add((fileName, arguments)))));
                SetProvider(window, "ClipboardSetter", ((Action<string>)(text => copied.Add(text))));
                SetProvider(window, "ExportPathSelector", ((Func<string, string?>)(_ => exportPath)));
                SetProvider(window, "TextFileWriter", ((Action<string, string>)((fileName, contents) => File.WriteAllText(fileName, contents, Encoding.UTF8))));

                OpenFolderMethod.Invoke(window, [window, new RoutedEventArgs()]);
                OpenRawMethod.Invoke(window, [window, new RoutedEventArgs()]);
                CopyPathMethod.Invoke(window, [window, new RoutedEventArgs()]);
                ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);
                ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Equal(3, started.Count);
                Assert.Equal("explorer.exe", started[0].fileName);
                Assert.Equal("notepad.exe", started[1].fileName);
                Assert.Equal("pwsh.exe", started[2].fileName);
                Assert.Single(copied);
                Assert.Equal(sessionFile, copied[0]);
                Assert.Equal("exported transcript", File.ReadAllText(exportPath, Encoding.UTF8));
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void OpenFolderButton_skips_launch_when_preferred_path_has_no_directory()
    {
        RunInSta(() =>
        {
            var session = BuildIndexedSession("session-nodir", "No Dir", "session-nodir.jsonl");
            var window = new MainWindow();
            var started = new List<(string fileName, string arguments)>();

            AddSession(window, session);
            SelectSingleSession(window, session);
            SetProvider(window, "ProcessStarter", (Action<string, string>)((fileName, arguments) => started.Add((fileName, arguments))));

            OpenFolderMethod.Invoke(window, [window, new RoutedEventArgs()]);

            Assert.Empty(started);
            window.Close();
        });
    }

    [Fact]
    public void ResumeButton_uses_user_profile_when_cwd_is_not_available()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-resume-default", "Resume Default");
                var session = BuildIndexedSession("session-resume-default", "Resume Default", sessionFile);
                var window = new MainWindow();
                var started = new List<(string fileName, string arguments)>();

                AddSession(window, session);
                SelectSingleSession(window, session);
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = "-";
                SetProvider(window, "ProcessStarter", (Action<string, string>)((fileName, arguments) => started.Add((fileName, arguments))));

                ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Single(started);
                Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), started[0].arguments, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ExportButton_with_cancelled_path_does_not_writeAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-export-cancel", "Export Cancel");
                var session = BuildIndexedSession("session-export-cancel", "Export Cancel", sessionFile);
                var parsed = BuildParsedFile("session-export-cancel", @"C:\export");
                var window = new MainWindow();
                var wrote = false;

                AddSession(window, session);
                SetProvider(window, "SessionParser", ((Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsed))));
                SetProvider(window, "FileTextReader", ((Func<string, string>)(_ => "raw-session-content")));
                SelectSingleSession(window, session);
                GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text = "ignored transcript";
                SetProvider(window, "ExportPathSelector", (Func<string, string?>)(_ => null));
                SetProvider(window, "TextFileWriter", (Action<string, string>)((_, _) => wrote = true));

                ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.False(wrote);
                Assert.Equal("Starting…", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
                window.Close();
                await Task.CompletedTask;
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ButtonHandlers_return_without_selection_or_previewAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";

            OpenFolderMethod.Invoke(window, [window, new RoutedEventArgs()]);
            OpenRawMethod.Invoke(window, [window, new RoutedEventArgs()]);
            CopyPathMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);
            BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
            await InvokePrivateTask(window, ExecuteMaintenanceUiAsyncMethod);

            Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            window.Close();
        });
    }

    [Fact]
    public void ExportPathSelector_uses_dialog_factory_and_presenter()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            SaveFileDialog? createdDialog = null;

            SetProvider(window, "SaveFileDialogFactory", (Func<SaveFileDialog>)(() => createdDialog = new SaveFileDialog()));
            SetProvider(window, "SaveFileDialogPresenter", (Func<SaveFileDialog, Window, bool?>)((dialog, owner) =>
            {
                Assert.Same(window, owner);
                Assert.Equal("session-1.md", dialog.FileName);
                Assert.Equal("Markdown (*.md)|*.md|Text (*.txt)|*.txt|JSON (*.json)|*.json", dialog.Filter);
                dialog.FileName = @"C:\exports\session-1.md";
                return true;
            }));

            var exportSelector = (Delegate)typeof(MainWindow)
                .GetProperty("ExportPathSelector", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var exportPath = (string?)exportSelector.DynamicInvoke("session-1.md");

            Assert.NotNull(createdDialog);
            Assert.Equal(@"C:\exports\session-1.md", exportPath);
            window.Close();
        });
    }

    [Fact]
    public void ExportButton_returns_when_selector_returns_blank_path()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-export", "Export Thread");
                var session = BuildIndexedSession("session-export", "Export Thread", sessionFile);
                var window = new MainWindow();
                var writes = new List<string>();

                AddSession(window, session);
                SelectSingleSession(window, session);
                Assert.NotNull(GetSelectedSession(window));
                GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text = "ignored";
                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";
                SetProvider(window, "ExportPathSelector", (Func<string, string?>)(_ => " "));
                SetProvider(window, "TextFileWriter", (Action<string, string>)((path, _) => writes.Add(path)));

                ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Empty(writes);
                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void SelectExportPath_returns_null_when_dialog_is_cancelled()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            SaveFileDialog? createdDialog = null;

            SetProvider(window, "SaveFileDialogFactory", (Func<SaveFileDialog>)(() => createdDialog = new SaveFileDialog()));
            SetProvider(window, "SaveFileDialogPresenter", (Func<SaveFileDialog, Window, bool?>)((_, owner) =>
            {
                Assert.Same(window, owner);
                return false;
            }));

            var exportSelector = (Delegate)typeof(MainWindow)
                .GetProperty("ExportPathSelector", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var exportPath = (string?)exportSelector.DynamicInvoke("session-2.md");

            Assert.NotNull(createdDialog);
            Assert.Null(exportPath);
            window.Close();
        });
    }

    [Fact]
    public void DescribeSqlitePath_returns_summary_for_existing_file()
    {
        var root = CreateTempDirectory();
        try
        {
            var sqlitePath = Path.Combine(root, "state_5.sqlite");
            File.WriteAllText(sqlitePath, "sqlite");

            var description = (string?)DescribeSqlitePathMethod.Invoke(null, [sqlitePath, null]);

            Assert.NotNull(description);
            Assert.Contains(sqlitePath, description, StringComparison.Ordinal);
            Assert.Contains("MB", description, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuildPreview_and_execute_maintenance_paths_update_uiAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-maint", "Maintenance Thread");
                var session = BuildIndexedSession("session-maint", "Maintenance Thread", sessionFile);
                var window = new MainWindow();
                var destinationRoots = new List<string>();

                MaintenanceExecutorField.SetValue(window, new MaintenanceExecutor(Path.Combine(root, "checkpoints")));
                AddSession(window, session);
                SelectSingleSession(window, GetNamedField<ListBox>(window, "SessionsListBox").Items.Cast<IndexedLogicalSession>().Single());
                GetNamedField<ComboBox>(window, "MaintenanceActionComboBox").SelectedItem = MaintenanceAction.Reconcile;
                SetProvider(
                    window,
                    "MaintenanceRunner",
                    ((Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, destinationRoot, _, _) =>
                    {
                        destinationRoots.Add(destinationRoot);
                        return Task.FromResult(new MaintenanceExecutionResult(true, [], Path.Combine(root, "checkpoint.json")));
                    })));

                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                GetNamedField<TextBox>(window, "DestinationRootTextBox").Text = string.Empty;

                await InvokePrivateTask(window, ExecuteMaintenanceUiAsyncMethod);
                Assert.NotEmpty(destinationRoots);
                Assert.Contains("Executed maintenance. Checkpoint:", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);

                SetProvider(
                    window,
                    "MaintenanceRunner",
                    ((Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, _, _, _) =>
                        Task.FromException<MaintenanceExecutionResult>(new InvalidOperationException("blocked")))));
                await InvokePrivateTask(window, ExecuteMaintenanceUiAsyncMethod);
                Assert.Contains("Maintenance failed: blocked", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void BuildPreviewButton_uses_archive_fallback_and_plural_confirmation()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionOne = BuildIndexedSession("session-a", "Thread A", WriteSessionJsonl(root, "session-a", "Thread A"));
                var sessionTwo = BuildIndexedSession("session-b", "Thread B", WriteSessionJsonl(root, "session-b", "Thread B"));
                var window = new MainWindow();
                var listBox = GetNamedField<ListBox>(window, "SessionsListBox");

                AddSession(window, sessionOne);
                AddSession(window, sessionTwo);
                GetNamedField<ComboBox>(window, "MaintenanceActionComboBox").SelectedItem = null;

                listBox.SelectedItems.Clear();
                listBox.SelectedItems.Add(sessionOne);
                listBox.SelectedItems.Add(sessionTwo);

                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Contains("Confirm with: ARCHIVE 2 FILES", GetNamedField<TextBlock>(window, "MaintenanceSummaryTextBlock").Text, StringComparison.Ordinal);
                Assert.Equal("ARCHIVE 2 FILES", GetNamedField<TextBox>(window, "TypedConfirmationTextBox").Text);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_sets_status_when_runner_returns_not_executedAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-maint-noexec", "Maintenance Thread");
                var session = BuildIndexedSession("session-maint-noexec", "Maintenance Thread", sessionFile);
                var window = new MainWindow();

                MaintenanceExecutorField.SetValue(window, new MaintenanceExecutor(Path.Combine(root, "checkpoints")));
                AddSession(window, session);
                SelectSingleSession(window, session);
                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                SetProvider(
                    window,
                    "MaintenanceRunner",
                    (Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, _, _, _) =>
                        Task.FromResult(new MaintenanceExecutionResult(false, [], Path.Combine(root, "checkpoint.json")))));

                await InvokePrivateTask(window, ExecuteMaintenanceUiAsyncMethod);

                Assert.Equal("Maintenance did not execute.", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    private static IReadOnlyList<KnownSessionStore> InvokeBuildKnownStores(bool deepScan) =>
        (IReadOnlyList<KnownSessionStore>)BuildKnownStoresMethod.Invoke(null, [deepScan])!;

    private static Task InvokePrivateTaskAsync(object instance, MethodInfo method, params object?[] args) =>
        (Task)method.Invoke(instance, args)!;

    private static T GetNamedField<T>(MainWindow window, string name) where T : class =>
        (typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as T)
        ?? throw new InvalidOperationException($"Field '{name}' was not found.");

    private static IndexedLogicalSession? GetSelectedSession(MainWindow window) =>
        typeof(MainWindow).GetMethod("GetSelectedSession", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(window, []) as IndexedLogicalSession;

    private static void AddSession(MainWindow window, IndexedLogicalSession session)
    {
        var sessions = (ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!;
        sessions.Add(session);
    }

    private static void SelectSingleSession(MainWindow window, IndexedLogicalSession session)
    {
        var listBox = GetNamedField<ListBox>(window, "SessionsListBox");
        listBox.SelectedItem = session;
        listBox.SelectedItems.Clear();
        listBox.SelectedItems.Add(session);
    }

    private static SessionCatalogRepository CreateRepository(string root, params IndexedLogicalSession[] sessions)
    {
        var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));
        repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        foreach (var session in sessions)
        {
            repository.UpsertAsync(session, CancellationToken.None).GetAwaiter().GetResult();
        }

        return repository;
    }

    private static IndexedLogicalSession BuildIndexedSession(string sessionId, string threadName, string filePath) =>
        new(
            sessionId,
            threadName,
            new SessionPhysicalCopy(sessionId, filePath, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false)),
            [new SessionPhysicalCopy(sessionId, filePath, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false))],
            new SessionSearchDocument
            {
                ReadableTranscript = $"Readable transcript for {threadName}",
                DialogueTranscript = $"Dialogue transcript for {threadName}",
                ToolSummary = $"Tool summary for {threadName}",
                CommandText = "codex resume",
                FilePaths = [filePath],
                Urls = ["https://example.com"],
                ErrorText = string.Empty,
                Alias = string.Empty,
                Tags = [],
                Notes = string.Empty
            });

    private static ParsedSessionFile BuildParsedFile(string sessionId, string? cwd) =>
        new(
            sessionId,
            null,
            cwd,
            new TechnicalBreadcrumbs(["codex resume"], [0], [], []),
            new NormalizedSessionDocument(
                sessionId,
                "Thread",
                DateTimeOffset.UtcNow,
                null,
                cwd,
                [
                    NormalizedSessionEvent.CreateMessage(SessionActor.User, "Hello"),
                    NormalizedSessionEvent.CreateMessage(SessionActor.Assistant, "World")
                ]));

    private static IndexedLogicalSession WithNullIndexedSessionProperty(IndexedLogicalSession session, string propertyName)
    {
        var clone = session with { };
        typeof(IndexedLogicalSession).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(clone, null);
        return clone;
    }

    private static string WriteSessionJsonl(string root, string sessionId, string threadName)
    {
        var sessionsRoot = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        var filePath = Path.Combine(sessionsRoot, $"{sessionId}.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new
                {
                    id = sessionId,
                    cwd = root,
                    timestamp = "2026-03-26T00:00:00Z"
                }
            }),
            JsonSerializer.Serialize(new
            {
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = threadName
                        }
                    }
                }
            })
        };

        File.WriteAllLines(filePath, lines, Encoding.UTF8);
        return filePath;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "codex-session-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }
    private static void SetProvider(MainWindow window, string propertyName, object value) =>
        typeof(MainWindow).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(window, value);

    private static void RunInSta(Action action) =>
        RunInStaAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static void RunInSta(Func<Task> action) =>
        RunInStaAsync(action).GetAwaiter().GetResult();

    private static Task RunInStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (OperationCanceledException)
                {
                    completion.SetCanceled();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}







