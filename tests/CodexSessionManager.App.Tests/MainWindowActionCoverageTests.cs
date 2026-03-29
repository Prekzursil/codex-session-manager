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
                var started = new List<(string fileName, IReadOnlyList<string> arguments)>();
                var copied = new List<string>();
                var exportPath = Path.Combine(root, "export.md");

                AddSession(window, session);
                SelectSingleSession(window, GetNamedField<ListBox>(window, "SessionsListBox").Items.Cast<IndexedLogicalSession>().Single());
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = @"C:\repo";
                GetNamedField<TextBox>(window, "ReadableTranscriptTextBox").Text = "exported transcript";
                SetProvider(window, "ProcessStarter", ((Action<string, IReadOnlyList<string>>)((fileName, arguments) => started.Add((fileName, arguments)))));
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
                Assert.Equal("codex", started[2].fileName);
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
            var started = new List<(string fileName, IReadOnlyList<string> arguments)>();

            AddSession(window, session);
            SelectSingleSession(window, session);
            SetProvider(window, "ProcessStarter", (Action<string, IReadOnlyList<string>>)((fileName, arguments) => started.Add((fileName, arguments))));

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
                var started = new List<(string fileName, IReadOnlyList<string> arguments)>();

                AddSession(window, session);
                SelectSingleSession(window, session);
                GetNamedField<TextBlock>(window, "CwdTextBlock").Text = "-";
                SetProvider(window, "ProcessStarter", (Action<string, IReadOnlyList<string>>)((fileName, arguments) => started.Add((fileName, arguments))));

                ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Single(started);
                Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), started[0].arguments, StringComparer.OrdinalIgnoreCase);
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
            await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);

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
    public void DescribeSqlitePath_single_argument_overload_returns_summary_for_existing_file()
    {
        var root = CreateTempDirectory();
        try
        {
            var sqlitePath = Path.Combine(root, "state_5.sqlite");
            File.WriteAllText(sqlitePath, "sqlite");

            var description = (string?)DescribeSqlitePathSingleArgumentMethod.Invoke(null, [sqlitePath]);

            Assert.NotNull(description);
            Assert.Contains(sqlitePath, description, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void SearchCancellation_helpers_replace_and_release_current_source()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            var firstToken = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;
            var firstSource = Assert.IsType<CancellationTokenSource>(
                CurrentSearchCancellationTokenSourceProperty.GetValue(window));

            var secondToken = (CancellationToken)BeginSearchTokenMethod.Invoke(window, [])!;
            var secondSource = Assert.IsType<CancellationTokenSource>(
                CurrentSearchCancellationTokenSourceProperty.GetValue(window));

            Assert.True(firstToken.IsCancellationRequested);
            Assert.NotSame(firstSource, secondSource);
            Assert.Throws<ObjectDisposedException>(() => _ = firstSource.Token.WaitHandle);

            ReleaseSearchCancellationStateMethod.Invoke(window, []);

            Assert.True(secondToken.IsCancellationRequested);
            Assert.Null(CurrentSearchCancellationTokenSourceProperty.GetValue(window));

            window.Close();
        });
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

                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);
                Assert.NotEmpty(destinationRoots);
                Assert.Contains("Executed maintenance. Checkpoint:", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);

                SetProvider(
                    window,
                    "MaintenanceRunner",
                    ((Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, _, _, _) =>
                        Task.FromException<MaintenanceExecutionResult>(new InvalidOperationException("blocked")))));
                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);
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

                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);

                Assert.Equal("Maintenance did not execute.", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
}
