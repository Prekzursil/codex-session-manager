using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.App.Tests;

public sealed partial class MainWindowCoverageTests
{
    [Fact]
    public void GetSelectedSessions_returns_selected_items()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionOne = BuildIndexedSession(
                    "session-selected-a",
                    "Selected A",
                    WriteSessionJsonl(root, "session-selected-a", "Selected A"));
                var sessionTwo = BuildIndexedSession(
                    "session-selected-b",
                    "Selected B",
                    WriteSessionJsonl(root, "session-selected-b", "Selected B"));
                var window = new MainWindow();
                var listBox = GetNamedField<ListBox>(window, "SessionsListBox");

                AddSession(window, sessionOne);
                AddSession(window, sessionTwo);
                listBox.SelectedItems.Add(sessionOne);
                listBox.SelectedItems.Add(sessionTwo);

                var selectedSessions = (IReadOnlyList<IndexedLogicalSession>)GetSelectedSessionsMethod.Invoke(window, [])!;

                Assert.Equal(2, selectedSessions.Count);
                Assert.Contains(selectedSessions, session => session.SessionId == "session-selected-a");
                Assert.Contains(selectedSessions, session => session.SessionId == "session-selected-b");
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SessionsListBox_OnSelectionChanged_invokes_session_loadingAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-handler-load", "Handler Load");
                var parsed = BuildParsedFile("session-handler-load", @"C:\handler");
                var session = BuildIndexedSession("session-handler-load", "Handler Load", sessionFile);
                var window = new MainWindow();

                AddSession(window, session);
                SelectSingleSession(window, session);
                SetProvider(window, "LiveSqliteStatusProvider", (() => "sqlite handler"));
                SetProvider(
                    window,
                    "SessionParser",
                    (Func<string, CancellationToken, Task<ParsedSessionFile>>)((_, _) => Task.FromResult(parsed)));
                SetProvider(window, "FileTextReader", (Func<string, string>)(_ => "handler-raw"));

                SessionsListSelectionChangedMethod.Invoke(
                    window,
                    [
                        window,
                        new SelectionChangedEventArgs(
                            Selector.SelectionChangedEvent,
                            Array.Empty<object>(),
                            Array.Empty<object>()),
                    ]);

                for (var attempt = 0; attempt < 50; attempt++)
                {
                    if (GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text == "Handler Load")
                    {
                        break;
                    }

                    await Task.Delay(10);
                }

                Assert.Equal("Handler Load", GetNamedField<TextBlock>(window, "ThreadNameTextBlock").Text);
                Assert.Equal("sqlite handler", GetNamedField<TextBlock>(window, "SQLiteStatusTextBlock").Text);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SearchTextBox_OnTextChanged_invokes_search_filteringAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionOne = BuildIndexedSession(
                    "session-search-handler-one",
                    "Renderer work",
                    WriteSessionJsonl(root, "session-search-handler-one", "Renderer work"));
                var sessionTwo = BuildIndexedSession(
                    "session-search-handler-two",
                    "Maintenance",
                    WriteSessionJsonl(root, "session-search-handler-two", "Maintenance"));
                var repository = CreateRepository(root, sessionOne, sessionTwo);
                var window = new MainWindow();

                RepositoryField.SetValue(window, repository);
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
                GetNamedField<TextBox>(window, "SearchTextBox").Text = "maint";

                SearchTextChangedMethod.Invoke(
                    window,
                    [window, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None)]);

                for (var attempt = 0; attempt < 50; attempt++)
                {
                    if (GetNamedField<ListBox>(window, "SessionsListBox").Items.Count == 1)
                    {
                        break;
                    }

                    await Task.Delay(10);
                }

                Assert.Single(GetNamedField<ListBox>(window, "SessionsListBox").Items);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SaveMetadataButton_OnClick_invokes_metadata_saveAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var session = BuildIndexedSession(
                    "session-save-handler",
                    "Save Handler",
                    WriteSessionJsonl(root, "session-save-handler", "Save Handler"));
                var repository = CreateRepository(root, session);
                var window = new MainWindow();

                RepositoryField.SetValue(window, repository);
                await InvokePrivateTaskAsync(window, LoadSessionsFromCatalogAsyncMethod);
                SelectSingleSession(
                    window,
                    GetNamedField<ListBox>(window, "SessionsListBox")
                        .Items.Cast<IndexedLogicalSession>()
                        .Single());
                GetNamedField<TextBox>(window, "AliasTextBox").Text = "Saved Alias";
                GetNamedField<TextBox>(window, "TagsTextBox").Text = "ops, handler";
                GetNamedField<TextBox>(window, "NotesTextBox").Text = "Saved from handler";

                SaveMetadataButtonMethod.Invoke(window, [window, new RoutedEventArgs()]);

                IndexedLogicalSession? refreshed = null;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    refreshed = (await repository.ListSessionsAsync(CancellationToken.None)).Single();
                    if (refreshed.SearchDocument.Alias == "Saved Alias")
                    {
                        break;
                    }

                    await Task.Delay(10);
                }

                Assert.NotNull(refreshed);
                Assert.Equal("Saved Alias", refreshed!.SearchDocument.Alias);
                Assert.Equal(["ops", "handler"], refreshed.SearchDocument.Tags);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ExecuteMaintenanceButton_OnClick_invokes_maintenance_runnerAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var session = BuildIndexedSession(
                    "session-maint-handler",
                    "Maintenance Handler",
                    WriteSessionJsonl(root, "session-maint-handler", "Maintenance Handler"));
                var window = new MainWindow();

                MaintenanceExecutorField.SetValue(
                    window,
                    new MaintenanceExecutor(Path.Combine(root, "checkpoints")));
                AddSession(window, session);
                SelectSingleSession(window, session);
                SetProvider(
                    window,
                    "MaintenanceRunner",
                    (Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, _, _, _) =>
                        Task.FromResult(new MaintenanceExecutionResult(true, [], Path.Combine(root, "handler-checkpoint.json")))));

                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                ExecuteMaintenanceButtonMethod.Invoke(window, [window, new RoutedEventArgs()]);

                for (var attempt = 0; attempt < 50; attempt++)
                {
                    if (GetNamedField<TextBlock>(window, "StatusTextBlock").Text.Contains("Executed maintenance.", StringComparison.Ordinal))
                    {
                        break;
                    }

                    await Task.Delay(10);
                }

                Assert.Contains("Executed maintenance.", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }
}
