using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using CodexSessionManager.App;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Parsing;
using Microsoft.Win32;

namespace CodexSessionManager.App.Tests;

[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
    private static readonly MethodInfo GetKnownStoresMethod =
        typeof(MainWindow).GetMethod("GetKnownStores", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildKnownStores_honors_deep_scan_and_includes_extra_codex_homes()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primaryCodexHome = Path.Combine(userProfile, ".codex");
        var extraCodexHome = Path.Combine(userProfile, $".codex-coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(primaryCodexHome);
        Directory.CreateDirectory(extraCodexHome);

        try
        {
            var shallowStores = InvokeBuildKnownStores(deepScan: false);
            Assert.DoesNotContain(
                shallowStores,
                store => string.Equals(store.WorkspaceRoot, extraCodexHome, StringComparison.OrdinalIgnoreCase));

            var deepStores = InvokeBuildKnownStores(deepScan: true);
            Assert.Contains(
                deepStores,
                store => string.Equals(store.WorkspaceRoot, extraCodexHome, StringComparison.OrdinalIgnoreCase)
                    && store.StoreKind == SessionStoreKind.Live);
            Assert.Contains(
                deepStores,
                store => string.Equals(store.WorkspaceRoot, extraCodexHome, StringComparison.OrdinalIgnoreCase)
                    && store.StoreKind == SessionStoreKind.Backup);
            Assert.Equal(
                deepStores.Count,
                deepStores
                    .Select(store => store.SessionsPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
        }
        finally
        {
            if (Directory.Exists(extraCodexHome))
            {
                Directory.Delete(extraCodexHome, recursive: true);
            }
        }
    }

    [Fact]
    public void DescribeSqlitePath_returns_null_for_missing_or_inaccessible_paths()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");

        Assert.Null((string?)DescribeSqlitePathMethod.Invoke(null, [missingPath, (Func<string, FileInfo>)(path => new FileInfo(path))]));
        Assert.Null((string?)DescribeSqlitePathMethod.Invoke(null, [missingPath, (Func<string, FileInfo>)(_ => throw new IOException("blocked"))]));
        Assert.Null((string?)DescribeSqlitePathMethod.Invoke(null, [missingPath, (Func<string, FileInfo>)(_ => throw new UnauthorizedAccessException("blocked"))]));

        var nullTwoArgPathException = Assert.Throws<TargetInvocationException>(() =>
            DescribeSqlitePathMethod.Invoke(null, [null!, null]));
        Assert.IsType<ArgumentNullException>(nullTwoArgPathException.InnerException);

        var nullPathException = Assert.Throws<TargetInvocationException>(() =>
            DescribeSqlitePathSingleArgumentMethod.Invoke(null, [null!]));
        Assert.IsType<ArgumentNullException>(nullPathException.InnerException);
    }

    [Fact]
    public void GetLiveSqliteStatus_and_known_store_helpers_cover_additional_branches()
    {
        var defaultStatus = (string)GetLiveSqliteStatusMethod.Invoke(null, [])!;
        Assert.False(string.IsNullOrWhiteSpace(defaultStatus));

        var joinedStatus = (string)GetLiveSqliteStatusWithInputsMethod.Invoke(
            null,
            [SqliteStatusPaths, (Func<string, string?>)(path => path == "first" ? "first.sqlite" : "second.sqlite")])!;
        Assert.Equal($"first.sqlite{Environment.NewLine}second.sqlite", joinedStatus);

        var nullPathsException = Assert.Throws<TargetInvocationException>(() =>
            GetLiveSqliteStatusWithInputsMethod.Invoke(null, [null!, (Func<string, string?>)(_ => null)]));
        Assert.IsType<ArgumentNullException>(nullPathsException.InnerException);

        var nullDescribeException = Assert.Throws<TargetInvocationException>(() =>
            GetLiveSqliteStatusWithInputsMethod.Invoke(null, [SqliteStatusPaths, null!]));
        Assert.IsType<ArgumentNullException>(nullDescribeException.InnerException);

        var nullProviderException = Assert.Throws<TargetInvocationException>(() =>
            GetKnownStoresMethod.Invoke(null, [null!, false]));
        Assert.IsType<ArgumentNullException>(nullProviderException.InnerException);

        var missingStoresException = Assert.Throws<TargetInvocationException>(() =>
            GetKnownStoresMethod.Invoke(
                null,
                [(Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => null!), false]));
        Assert.IsType<InvalidOperationException>(missingStoresException.InnerException);

        var expectedStores = (IReadOnlyList<KnownSessionStore>)
        [
            new KnownSessionStore(
                WorkspaceRoot: @"C:\codex",
                StoreKind: SessionStoreKind.Live,
                SessionsPath: @"C:\codex\sessions",
                SessionIndexPath: @"C:\codex\session_index.jsonl"),
        ];

        var stores = (IReadOnlyList<KnownSessionStore>)GetKnownStoresMethod.Invoke(
            null,
            [(Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => expectedStores), true])!;
        Assert.Same(expectedStores, stores);
    }

    [Fact]
    public async Task Infrastructure_helpers_validate_null_delegates_and_success_pathsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            try
            {
                var nullUiTask = (Task)RunOnUiThreadAsyncMethod.Invoke(window, new object?[] { null })!;
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await nullUiTask);

                var nullValueTask = (Task<string>)RunOnUiThreadValueAsyncMethod.Invoke(window, new object?[] { null })!;
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await nullValueTask);

                var nullActionException = Assert.Throws<TargetInvocationException>(() =>
                    RunEventTaskMethod.Invoke(window, new object?[] { null, "Failure" }));
                Assert.IsType<ArgumentNullException>(nullActionException.InnerException);

                var ran = false;
                RunEventTaskMethod.Invoke(
                    window,
                    [(Func<Task>)(() =>
                    {
                        ran = true;
                        return Task.CompletedTask;
                    }), "Failure"]);

                for (var attempt = 0; attempt < 10 && !ran; attempt++)
                {
                    await Task.Delay(10);
                }

                Assert.True(ran);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Session_operation_helpers_cover_canceled_and_guarded_pathsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-cancel", "Cancel Thread");
                var session = BuildIndexedSession("session-cancel", "Cancel Thread", sessionFile);
                var repository = CreateRepository(root, session);
                var window = new MainWindow();
                var sessions = (ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!;

                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";
                await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);
                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                await InvokePrivateTaskAsync(window, ReloadSessionsForSearchAsyncMethod, CancellationToken.None);
                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                var missingRepositoryException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    InvokePrivateTaskAsync(window, ApplySearchResultsAsyncMethod, "Cancel", CancellationToken.None));
                Assert.Equal("Repository has not been initialized.", missingRepositoryException.Message);

                var nullSelectedHeaderException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, null!, session.SessionId));
                Assert.Equal("selected", nullSelectedHeaderException.ParamName);

                var blankSessionIdException = await Assert.ThrowsAsync<ArgumentException>(() =>
                    InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, session, " "));
                Assert.Equal("selectedSessionId", blankSessionIdException.ParamName);

                sessions.Add(session);
                SelectSingleSession(window, session);
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = "cwd";
                GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text = "sqlite";
                GetNamedField<TextBox>(window, "AuditTranscriptTextBox").Text = "audit";
                GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text = "raw";

                await InvokePrivateTaskAsync(
                    window,
                    LoadSelectedSessionBodyAsyncMethod,
                    WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy)),
                    session.SessionId);
                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal("Live SQLite status unavailable.", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Equal(string.Empty, GetNamedField<TextBox>(window, "AuditTranscriptTextBox").Text);
                Assert.Contains("Unable to load raw session content.", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);

                var nullSelectedBodyException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, null!, session.SessionId));
                Assert.Equal("selected", nullSelectedBodyException.ParamName);

                var nullBodySessionIdException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, null!));
                Assert.Equal("selectedSessionId", nullBodySessionIdException.ParamName);

                RepositoryField.SetValue(window, repository);
                sessions.Clear();
                sessions.Add(BuildIndexedSession("stale-session", "Stale Thread", sessionFile));
                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "canceled";
                var staleToken = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;
                _ = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;

                await InvokePrivateTaskAsync(window, ReloadSessionsForSearchAsyncMethod, staleToken);

                Assert.Single(sessions);
                Assert.Equal("stale-session", sessions.Single().SessionId);
                Assert.Equal("canceled", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                RepositoryField.SetValue(window, null);
                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);
                Assert.Equal("canceled", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task Session_operation_additional_stale_and_guard_branchesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var primarySessionFile = WriteSessionJsonl(root, "session-primary", "Primary Thread");
                var secondarySessionFile = WriteSessionJsonl(root, "session-secondary", "Secondary Thread");
                var primarySession = BuildIndexedSession("session-primary", "Primary Thread", primarySessionFile);
                var secondarySession = BuildIndexedSession("session-secondary", "Secondary Thread", secondarySessionFile);
                var repository = CreateRepository(root, primarySession, secondarySession);
                var parsedFile = BuildParsedFile("session-primary", @"C:\workspace");
                var window = new MainWindow();
                var sessions = (ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!;
                var headerSession = WithNullIndexedSessionProperty(
                    WithNullIndexedSessionProperty(primarySession, nameof(IndexedLogicalSession.ThreadName)),
                    nameof(IndexedLogicalSession.PhysicalCopies));

                AddSession(window, headerSession);
                AddSession(window, secondarySession);
                SelectSingleSession(window, headerSession);
                RepositoryField.SetValue(window, repository);
                SetProvider(window, "SessionParser", (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsedFile)));
                SetProvider(window, "FileTextReader", (Func<string, string>)(_ => "stale raw content"));
                SetProvider(window, "LiveSqliteStatusProvider", (Func<string>)(() => "stale sqlite"));

                await InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, headerSession, headerSession.SessionId);
                Assert.Equal(string.Empty, GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
                Assert.Empty(GetNamedField<ListBox>(window, "CopiesListBox").Items.Cast<SessionPhysicalCopy>());

                SetProvider(window, "SessionParser", (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(BuildParsedFile("session-primary", null))));
                SetProvider(window, "FileTextReader", (Func<string, string>)(_ => "cwd fallback raw"));
                SelectSingleSession(window, headerSession);
                await InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, headerSession, headerSession.SessionId);
                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);

                SelectSingleSession(window, secondarySession);
                GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text = "unchanged thread";
                await InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, primarySession, primarySession.SessionId);
                Assert.Equal("unchanged thread", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);

                GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text = "keep raw";
                await InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, primarySession, primarySession.SessionId);
                Assert.Equal("keep raw", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);

                SetProvider(window, "SessionParser", (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => throw new InvalidOperationException("stale parse failure")));
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = "keep cwd";
                GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text = "keep sqlite";
                GetNamedField<TextBox>(window, "AuditTranscriptTextBox").Text = "keep audit";
                GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text = "keep raw after stale failure";
                await InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, primarySession, primarySession.SessionId);
                Assert.Equal("keep cwd", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal("keep sqlite", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Equal("keep audit", GetNamedField<TextBox>(window, "AuditTranscriptTextBox").Text);
                Assert.Equal("keep raw after stale failure", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);

                sessions.Clear();
                sessions.Add(BuildIndexedSession("stale-search", "Stale Search", primarySessionFile));
                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "stale search";
                var staleSearchToken = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;
                _ = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;

                await (Task)ApplySearchResultsAsyncMethod.Invoke(window, new object?[] { null, staleSearchToken })!;

                Assert.Single(sessions);
                Assert.Equal("stale-search", sessions.Single().SessionId);
                Assert.Equal("stale search", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";
                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, null);
                await InvokePrivateTaskAsync(window, RefreshAsyncMethod, false);
                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "still idle";
                RepositoryField.SetValue(window, null);
                WorkspaceIndexerField.SetValue(window, new SessionWorkspaceIndexer(repository));
                await InvokePrivateTaskAsync(window, RefreshAsyncMethod, true);
                Assert.Equal("still idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "metadata idle";
                RepositoryField.SetValue(window, repository);
                SelectSingleSession(window, secondarySession);
                GetNamedField<ListBox>(window, "SessionsListBox").SelectedItems.Clear();
                GetNamedField<ListBox>(window, "SessionsListBox").SelectedItem = null;
                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);
                Assert.Equal("metadata idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);

                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void SelectExportPath_throws_when_dialog_factory_returns_null()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            try
            {
                SetProvider(window, "SaveFileDialogFactory", (Func<SaveFileDialog>)(() => null!));

                var exportSelector = (Delegate)typeof(MainWindow)
                    .GetProperty("ExportPathSelector", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;

                var exception = Assert.Throws<TargetInvocationException>(() => exportSelector.DynamicInvoke("session-null.md"));
                Assert.IsType<InvalidOperationException>(exception.InnerException);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
