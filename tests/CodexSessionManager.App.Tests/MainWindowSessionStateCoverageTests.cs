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

                await InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, session.SessionId);
                Assert.Equal(string.Empty, GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ApplySearchResultsAsync_throws_when_repository_is_missingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InvokePrivateTaskAsync(window, ApplySearchResultsAsyncMethod, "query", CancellationToken.None));

            window.Close();
        });
    }

    [Fact]
    public async Task ApplySearchResultsAsync_normalizes_null_query_before_searchingAsync()
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

                await InvokePrivateTaskAsync(
                    window,
                    ApplySearchResultsAsyncMethod,
                    null!,
                    CancellationToken.None);

                Assert.Contains("Search returned", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task Search_result_helpers_skip_ui_updates_when_search_is_canceledAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-search-canceled", "Canceled Search");
                var repository = CreateRepository(
                    root,
                    BuildIndexedSession("session-search-canceled", "Canceled Search", sessionFile));
                var window = new MainWindow();

                RepositoryField.SetValue(window, repository);
                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";

                await InvokePrivateTaskAsync(
                    window,
                    ReloadSessionsForSearchAsyncMethod,
                    new CancellationToken(canceled: true));
                await InvokePrivateTaskAsync(
                    window,
                    ApplySearchResultsAsyncMethod,
                    "Canceled",
                    new CancellationToken(canceled: true));

                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
                Assert.Empty(GetNamedField<ListBox>(window, "SessionsListBox").Items);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ReloadSessionsForSearchAsync_returns_without_repositoryAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";

            await InvokePrivateTaskAsync(
                window,
                ReloadSessionsForSearchAsyncMethod,
                CancellationToken.None);

            Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            window.Close();
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

                await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);

                Assert.Equal("-", GetNamedField<TextBlock>(window, "CwdTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadSelectedSessionAsync_returns_without_selectionAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);

            Assert.Equal("Starting…", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            Assert.Equal("-", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
            window.Close();
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

                await InvokePrivateTaskAsync(window, LoadSelectedSessionAsyncMethod);

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
    public async Task LoadSelectedSessionBodyAsync_failure_skips_updates_when_session_is_no_longer_selectedAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-body-error-stale", "Body Error Stale");
                var window = new MainWindow();
                var session = BuildIndexedSession("session-body-error-stale", "Body Error Stale", sessionFile);
                var parserEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseParser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                AddSession(window, session);
                SelectSingleSession(window, session);
                GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text = "keep sqlite";
                GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text = "keep raw";
                SetProvider(
                    window,
                    "SessionParser",
                    (Func<string, CancellationToken, Task<ParsedSessionFile>>)(async (_, _) =>
                    {
                        parserEntered.SetResult();
                        await releaseParser.Task;
                        throw new InvalidOperationException("late parse failure");
                    }));

                var loadTask = InvokePrivateTaskAsync(window, LoadSelectedSessionBodyAsyncMethod, session, session.SessionId);
                await parserEntered.Task;
                GetNamedField<ListBox>(window, "SessionsListBox").SelectedItem = null;
                releaseParser.SetResult();
                await loadTask;

                Assert.Equal("keep sqlite", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                Assert.Equal("keep raw", GetNamedField<TextBox>(window, "RawTranscriptTextBox").Text);
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

                await InvokePrivateTaskAsync(window, PopulateSelectedSessionHeaderAsyncMethod, session, session.SessionId);

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

                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);

                var searchBox = GetNamedField<TextBox>(window, "SearchTextBox");
                searchBox.Text = "maint";
                await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);

                searchBox.Text = string.Empty;
                await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
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
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);

                GetNamedField<TextBox>(window, "SearchTextBox").Text = "Thread";
                var searchTask = Task.Run(async () => await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod));

                CancellationTokenSource? searchCts = null;
                for (var attempt = 0; attempt < 50 && searchCts is null; attempt++)
                {
                    searchCts = CurrentSearchCancellationTokenSourceProperty.GetValue(window) as CancellationTokenSource;
                    if (searchCts is null)
                    {
                        await Task.Delay(10);
                    }
                }

                Assert.NotNull(searchCts);
                searchCts!.Cancel();
                await searchTask;

                window.Close();
                Assert.Null(CurrentSearchCancellationTokenSourceProperty.GetValue(window));
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

            await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
            await InvokePrivateTaskAsync(window, SearchSessionsAsyncMethod);
            await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);

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
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);

                SelectSingleSession(window, GetNamedField<ListBox>(window, "SessionsListBox").Items.Cast<IndexedLogicalSession>().Single());
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "Ops Alias";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "ops, strict-zero";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "Updated note";

                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);

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
    public async Task SaveSelectedMetadataAsync_without_selection_returns_without_changesAsync()
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

                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);

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
    public async Task SaveSelectedMetadataAsync_returns_when_no_session_is_selectedAsync()
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
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "should not persist";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "ops,app";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "ignored note";

                var sessionsList = GetNamedField<ListBox>(window, "SessionsListBox");
                sessionsList.SelectedItem = null;
                sessionsList.SelectedItems.Clear();
                Assert.Null(GetSelectedSession(window));

                await InvokePrivateTaskAsync(window, SaveSelectedMetadataAsyncMethod);

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
}
