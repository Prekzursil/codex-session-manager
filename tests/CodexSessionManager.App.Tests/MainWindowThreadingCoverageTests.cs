#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
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

                await InvokePrivateTaskAsync(window, InitializeAsyncMethod);

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

            await InvokePrivateTaskAsync(window, InitializeAsyncMethod);

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

                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);

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

                await InvokePrivateTaskAsync(window, RefreshAsyncMethod, false);

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
                    """{"id":"session-refresh-deep","thread_name":"Refresh Thread Deep"}""" + Environment.NewLine,
                    Encoding.UTF8);

                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, indexer);
                SetProvider(window, "KnownStoresProvider", (Func<bool, IReadOnlyList<KnownSessionStore>>)(deepScan =>
                {
                    Assert.True(deepScan);
                    return stores;
                }));

                await InvokePrivateTaskAsync(window, RefreshAsyncMethod, true);

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
    public async Task RefreshAsync_throws_when_known_stores_provider_is_missingAsync()
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
                SetProvider(window, "KnownStoresProvider", null!);

                await Assert.ThrowsAsync<ArgumentNullException>(() => InvokePrivateTaskAsync(window, RefreshAsyncMethod, false));
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task RefreshAsync_throws_when_known_stores_provider_returns_nullAsync()
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
                    (Func<bool, IReadOnlyList<KnownSessionStore>>)(_ => null!));

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    InvokePrivateTaskAsync(window, RefreshAsyncMethod, false));
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

                await InvokePrivateTaskAsync(window, RunBackgroundRefreshAsyncMethod);

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

                await InvokePrivateTaskAsync(window, RunBackgroundRefreshAsyncMethod);

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

                await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);

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
                await Assert.ThrowsAsync<InvalidOperationException>(() => InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy)), session.SessionId));
                await Assert.ThrowsAsync<InvalidOperationException>(() => InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.SearchDocument)), session.SessionId));
                await InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PhysicalCopies)), session.SessionId);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task PopulateSelectedSessionHeaderAsync_uses_empty_copy_list_when_copies_are_missingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-header-empty-copies", "Header Empty Copies");
                var session = WithNullIndexedSessionProperty(
                    BuildIndexedSession("session-header-empty-copies", "Header Empty Copies", sessionFile),
                    nameof(IndexedLogicalSession.PhysicalCopies));
                var window = new MainWindow();

                AddSession(window, session);
                SelectSingleSession(window, session);

                await InvokePrivateTaskAsync(
                    window,
                    PopulateSelectedSessionHeaderAsyncMethod,
                    session,
                    session.SessionId);

                Assert.Empty(GetNamedField<ListBox>(window, "CopiesListBox").Items);
                Assert.Equal("Header Empty Copies", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task Session_detail_helpers_reject_null_inputsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-detail-null", "Detail Null");
                var session = BuildIndexedSession("session-detail-null", "Detail Null", sessionFile);
                var window = new MainWindow();

                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, null!, session.SessionId));
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, session, null!));
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, null!, session.SessionId));
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, null!));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, " "));
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionBodyAsync_skips_updates_when_session_is_no_longer_selectedAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-body-stale", "Body Stale");
                var session = BuildIndexedSession("session-body-stale", "Body Stale", sessionFile);
                var parsed = BuildParsedFile("session-body-stale", @"C:\stale");
                var window = new MainWindow();

                SetProvider(window, "LiveSqliteStatusProvider", (() => "sqlite stale"));
                SetProvider(
                    window,
                    "SessionParser",
                    ((Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsed))));
                SetProvider(window, "FileTextReader", ((Func<string, string>)(_ => "stale-content")));

                await InvokePrivateTaskAsync(
                    window,
                    LoadSelectedSessionBodyAsyncMethod,
                    session,
                    session.SessionId);

                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
                Assert.Equal(string.Empty, GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }
}
