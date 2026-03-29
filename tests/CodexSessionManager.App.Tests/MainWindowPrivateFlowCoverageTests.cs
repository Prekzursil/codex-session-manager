#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using CodexSessionManager.App;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.App.Tests;

[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
    [Fact]
    public async Task RunOnUiThread_helpers_cover_inline_and_background_dispatchAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            var statusTextBlock = GetNamedField<TextBlock>(window, "StatusTextBlock");

            await InvokePrivateTaskAsync(window, RunOnUiThreadAsyncMethod, (Action)(() => statusTextBlock.Text = "inline"));
            var inlineValue = await (Task<string>)RunOnUiThreadValueAsyncMethod.Invoke(window, [(Func<string>)(() => statusTextBlock.Text)])!;

            string? backgroundValue = null;
            await Task.Run(async () =>
            {
                await InvokePrivateTaskAsync(window, RunOnUiThreadAsyncMethod, (Action)(() => statusTextBlock.Text = "background"));
                backgroundValue = await (Task<string>)RunOnUiThreadValueAsyncMethod.Invoke(window, [(Func<string>)(() => statusTextBlock.Text)])!;
            });

            Assert.Equal("inline", inlineValue);
            Assert.Equal("background", backgroundValue);
            window.Close();
        });
    }

    [Fact]
    public async Task RunEventTask_reports_failures_and_validates_failure_prefixAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            var prefixException = Assert.Throws<TargetInvocationException>(() =>
                RunEventTaskMethod.Invoke(window, [(Func<Task>)(() => Task.CompletedTask), " "]));
            Assert.IsType<ArgumentException>(prefixException.InnerException);

            RunEventTaskMethod.Invoke(
                window,
                [(Func<Task>)(() => Task.FromException(new InvalidOperationException("boom"))), "Failed action"]);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (GetNamedField<TextBlock>(window, "StatusTextBlock").Text.Contains("Failed action: boom", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(10);
            }

            Assert.Contains("Failed action: boom", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            window.Close();
        });
    }

    [Fact]
    public async Task InitializeAsync_sets_defaults_and_reports_failuresAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var initializedWindow = new MainWindow();
                var scheduledRefreshCount = 0;

                SetProvider(initializedWindow, "LocalDataRootProvider", (Func<string>)(() => root));
                SetProvider(initializedWindow, "ScheduleRefreshAction", (Action)(() => scheduledRefreshCount++));
                SetProvider(initializedWindow, "RepositoryFactory", (Func<string, SessionCatalogRepository>)(path => new SessionCatalogRepository(path)));
                SetProvider(initializedWindow, "WorkspaceIndexerFactory", (Func<SessionCatalogRepository, SessionWorkspaceIndexer>)(repository => new SessionWorkspaceIndexer(repository)));
                SetProvider(initializedWindow, "MaintenanceExecutorFactory", (Func<string, MaintenanceExecutor>)(path => new MaintenanceExecutor(path)));

                await InvokePrivateTaskAsync(initializedWindow, InitializeAsyncMethod);

                Assert.Equal(Path.Combine(root, "maintenance", "archive"), GetNamedField<TextBox>(initializedWindow, "DestinationRootTextBox").Text);
                Assert.Equal(1, scheduledRefreshCount);
                Assert.NotNull(RepositoryField.GetValue(initializedWindow));
                Assert.NotNull(WorkspaceIndexerField.GetValue(initializedWindow));
                Assert.NotNull(MaintenanceExecutorField.GetValue(initializedWindow));
                Assert.Contains("Loaded 0 sessions", GetNamedField<TextBlock>(initializedWindow, "StatusTextBlock").Text, StringComparison.Ordinal);

                var failingWindow = new MainWindow();
                SetProvider(failingWindow, "LocalDataRootProvider", (Func<string>)(() => throw new InvalidOperationException("startup blocked")));

                await InvokePrivateTaskAsync(failingWindow, InitializeAsyncMethod);

                Assert.Contains("Startup failed: startup blocked", GetNamedField<TextBlock>(failingWindow, "StatusTextBlock").Text, StringComparison.Ordinal);
                initializedWindow.Close();
                failingWindow.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSessionsFromCatalogAsync_and_search_paths_cover_repository_flowsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionOneFile = WriteSessionJsonl(root, "session-alpha", "Alpha Thread");
                var sessionTwoFile = WriteSessionJsonl(root, "session-beta", "Beta Thread");
                var repository = CreateRepository(
                    root,
                    BuildIndexedSession("session-alpha", "Alpha Thread", sessionOneFile),
                    BuildIndexedSession("session-beta", "Beta Thread", sessionTwoFile));
                var window = new MainWindow();

                var sessions = (ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!;
                sessions.Add(BuildIndexedSession("stale-session", "Stale Thread", sessionOneFile));
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
                Assert.Single(sessions);

                RepositoryField.SetValue(window, repository);
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
                Assert.Equal(2, sessions.Count);

                GetNamedField<TextBox>(window, "SearchTextBox").Text = string.Empty;
                await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
                Assert.Equal(2, sessions.Count);

                GetNamedField<TextBox>(window, "SearchTextBox").Text = "Alpha";
                await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
                Assert.Single(sessions);
                Assert.Equal("session-alpha", sessions.Single().SessionId);

                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "unchanged";
                RepositoryField.SetValue(window, null);
                await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
                Assert.Equal("unchanged", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RunBackgroundRefreshAsync_reports_failures_from_refreshAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var window = new MainWindow();
                var repository = CreateRepository(root);

                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, new SessionWorkspaceIndexer(repository));
                SetProvider(
                    window,
                    "KnownStoresProvider",
                    (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => throw new InvalidOperationException("refresh blocked")));

                await InvokePrivateTaskAsync(window, RunBackgroundRefreshAsyncMethod);

                Assert.Contains("Background refresh failed: refresh blocked", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionAsync_and_metadata_paths_cover_selection_flowsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-selection", "Selection Thread");
                var session = BuildIndexedSession("session-selection", "Selection Thread", sessionFile);
                var repository = CreateRepository(root, session);
                var parsedFile = BuildParsedFile("session-selection", @"C:\repo");
                var window = new MainWindow();

                AddSession(window, session);
                SetProvider(window, "SessionParser", (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsedFile)));
                SetProvider(window, "FileTextReader", (Func<string, string>)(_ => "raw session content"));
                SetProvider(window, "LiveSqliteStatusProvider", (Func<string>)(() => "sqlite ready"));
                SelectSingleSession(window, session);

                await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);

                Assert.Equal("Selection Thread", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
                Assert.Equal("session-selection", GetNamedField<TextBlock>(window, "SessionIdTextBlock").Text);
                Assert.Equal(sessionFile, GetNamedField<TextBlock>(window, "PreferredPathTextBlock").Text);
                Assert.Equal(@"C:\repo", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal("sqlite ready", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Equal("raw session content", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);
                Assert.Contains("Hello", GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text, StringComparison.Ordinal);
                Assert.Contains("World", GetNamedField<TextBox>(window, "DialogueTranscriptTextBox").Text, StringComparison.Ordinal);
                Assert.NotEmpty(GetNamedField<TextBox>(window, "AuditTranscriptTextBox").Text);

                SetProvider(window, "SessionParser", (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => throw new InvalidOperationException("parse failed")));
                await InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, session.SessionId);
                Assert.Contains("Unable to load raw session content.", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);
                Assert.Contains("parse failed", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text, StringComparison.Ordinal);
                Assert.Equal("Live SQLite status unavailable.", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);

                RepositoryField.SetValue(window, repository);
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "alias";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "one, two";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "notes";
                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);

                var reloadedSession = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));
                Assert.Equal("alias", reloadedSession.SearchDocument.Alias);
                Assert.Collection(
                    reloadedSession.SearchDocument.Tags,
                    tag => Assert.Equal("one", tag),
                    tag => Assert.Equal("two", tag));
                Assert.Equal("notes", reloadedSession.SearchDocument.Notes);
                Assert.Contains("Saved metadata for session-selection.", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task Private_selection_helpers_cover_required_guard_branchesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-guard", "Guard Thread");
                var session = BuildIndexedSession("session-guard", "Guard Thread", sessionFile);
                var window = new MainWindow();

                var missingPreferredCopyException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    InvokePrivateTaskAsync(
                        window,
                        PopulateSelectedSessionHeaderAsyncMethod,
                        WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy)),
                        session.SessionId));
                Assert.Equal("Selected session is missing a preferred copy.", missingPreferredCopyException.Message);

                var missingSearchDocumentException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    InvokePrivateTaskAsync(
                        window,
                        PopulateSelectedSessionHeaderAsyncMethod,
                        WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.SearchDocument)),
                        session.SessionId));
                Assert.Equal("Selected session is missing search metadata.", missingSearchDocumentException.Message);

                var nullSessionException = Assert.Throws<TargetInvocationException>(() =>
                    GetRequiredPreferredCopyMethod.Invoke(null, [null!]));
                Assert.IsType<ArgumentNullException>(nullSessionException.InnerException);

                var missingSessionIdException = Assert.Throws<TargetInvocationException>(() =>
                    GetRequiredPreferredCopyMethod.Invoke(null, [WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy))]));
                Assert.IsType<InvalidOperationException>(missingSessionIdException.InnerException);

                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void GetLiveSqliteStatus_reports_no_live_store_when_all_descriptions_are_null()
    {
        var result = (string)GetLiveSqliteStatusWithInputsMethod.Invoke(null, [SqliteStatusPaths, (Func<string, string?>)(_ => null)])!;
        Assert.Equal("No live SQLite store detected.", result);
    }
}
