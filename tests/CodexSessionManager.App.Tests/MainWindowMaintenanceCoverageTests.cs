#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CodexSessionManager.App;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.App.Tests;

[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
    private static readonly string[] SuccessfulExitArguments = ["/c", "exit", "0"];

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
    public async Task ExecuteMaintenanceAsync_uses_default_runner_when_executor_is_configuredAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-default-runner", "Default Runner");
                var session = BuildIndexedSession("session-default-runner", "Default Runner", sessionFile);
                var window = new MainWindow();

                MaintenanceExecutorField.SetValue(window, new MaintenanceExecutor(Path.Combine(root, "checkpoints")));
                AddSession(window, session);
                SelectSingleSession(window, session);
                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                GetNamedField<TextBox>(window, "DestinationRootTextBox").Text = string.Empty;

                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);

                Assert.Contains("Executed maintenance. Checkpoint:", GetNamedField<TextBlock>(window, "StatusTextBlock").Text, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_returns_without_executor_when_preview_existsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-missing-executor", "Missing Executor");
                var session = BuildIndexedSession("session-missing-executor", "Missing Executor", sessionFile);
                var window = new MainWindow();

                AddSession(window, session);
                SelectSingleSession(window, session);
                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                GetNamedField<TextBlock>(window, "StatusTextBlock").Text = "idle";

                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);

                Assert.Equal("idle", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
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
    public void StartExternalProcess_rejects_blank_file_name_and_null_arguments()
    {
        var blankException = Assert.Throws<TargetInvocationException>(() =>
            StartExternalProcessMethod.Invoke(null, [" ", Array.Empty<string>()]));
        Assert.IsType<ArgumentException>(blankException.InnerException);

        var argumentsException = Assert.Throws<TargetInvocationException>(() =>
            StartExternalProcessMethod.Invoke(null, ["codex", null!]));
        Assert.IsType<ArgumentNullException>(argumentsException.InnerException);
    }

    [Fact]
    public void StartExternalProcess_rejects_unapproved_commands()
    {
        var invalidCommandException = Assert.Throws<TargetInvocationException>(() =>
            StartExternalProcessMethod.Invoke(null, ["powershell.exe", Array.Empty<string>()]));
        Assert.IsType<InvalidOperationException>(invalidCommandException.InnerException);
    }

    [Fact]
    public void NormalizeAllowedProcessFileName_accepts_bare_allowlisted_names_and_rejects_null()
    {
        var nullCandidateException = Assert.Throws<TargetInvocationException>(() =>
            NormalizeAllowedProcessFileNameMethod.Invoke(null, [null!]));
        Assert.IsType<ArgumentNullException>(nullCandidateException.InnerException);

        Assert.Equal(
            "explorer.exe",
            (string)NormalizeAllowedProcessFileNameMethod.Invoke(null, ["explorer.exe"])!);
        Assert.Equal(
            "notepad.exe",
            (string)NormalizeAllowedProcessFileNameMethod.Invoke(null, ["notepad.exe"])!);
        Assert.Equal(
            "codex",
            (string)NormalizeAllowedProcessFileNameMethod.Invoke(null, ["codex"])!);
    }

    [Fact]
    public void StartExternalProcess_starts_valid_process()
    {
        var fileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var exception = Record.Exception(() =>
            StartExternalProcessMethod.Invoke(
                null,
                [fileName, SuccessfulExitArguments]));

        Assert.Null(exception);
    }

    [Fact]
    public void StartExternalProcess_starts_valid_process_without_arguments()
    {
        var fileName = Path.Combine(Environment.SystemDirectory, "whoami.exe");

        var exception = Record.Exception(() =>
            StartExternalProcessMethod.Invoke(
                null,
                [fileName, Array.Empty<string>()]));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Refresh_buttons_invoke_background_refresh_with_expected_modesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var window = new MainWindow();
                var repository = CreateRepository(root);
                var observedModes = new List<bool>();

                RepositoryField.SetValue(window, repository);
                WorkspaceIndexerField.SetValue(window, new SessionWorkspaceIndexer(repository));
                SetProvider(
                    window,
                    "KnownStoresProvider",
                    (Func<bool, IReadOnlyList<KnownSessionStore>>)(deepScan =>
                    {
                        observedModes.Add(deepScan);
                        return Array.Empty<KnownSessionStore>();
                    }));

                RefreshButtonMethod.Invoke(window, [window, new RoutedEventArgs()]);
                DeepScanButtonMethod.Invoke(window, [window, new RoutedEventArgs()]);

                for (var attempt = 0; attempt < 50 && observedModes.Count < 2; attempt++)
                {
                    await Task.Delay(10);
                }

                Assert.Equal(2, observedModes.Count);
                Assert.Contains(false, observedModes);
                Assert.Contains(true, observedModes);
                Assert.Contains(
                    "Indexed 0 deduped sessions",
                    GetNamedField<TextBlock>(window, "StatusTextBlock").Text,
                    StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public void BuildPreview_ignores_selected_sessions_without_physical_copies()
    {
        RunInSta(() =>
        {
            var root = CreateTempDirectory();
            try
            {
                var firstSessionFile = WriteSessionJsonl(root, "session-preview-available", "Preview Available");
                var secondSessionFile = WriteSessionJsonl(root, "session-preview-empty", "Preview Empty");
                var window = new MainWindow();
                var availableSession = BuildIndexedSession(
                    "session-preview-available",
                    "Preview Available",
                    firstSessionFile);
                var emptySession = WithNullIndexedSessionProperty(
                    BuildIndexedSession(
                        "session-preview-empty",
                        "Preview Empty",
                        secondSessionFile),
                    nameof(IndexedLogicalSession.PhysicalCopies));
                var listBox = GetNamedField<ListBox>(window, "SessionsListBox");

                AddSession(window, availableSession);
                AddSession(window, emptySession);
                listBox.SelectedItems.Clear();
                listBox.SelectedItems.Add(availableSession);
                listBox.SelectedItems.Add(emptySession);

                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);

                Assert.Contains(
                    "Confirm with: ARCHIVE 1 FILE",
                    GetNamedField<TextBlock>(window, "MaintenanceSummaryTextBlock").Text,
                    StringComparison.Ordinal);
                Assert.Equal(
                    "ARCHIVE 1 FILE",
                    GetNamedField<TextBox>(window, "TypedConfirmationTextBox").Text);
                window.Close();
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task MaintenanceRunner_throws_when_executor_is_not_initializedAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-missing-runner", "Missing Runner");
                var session = BuildIndexedSession("session-missing-runner", "Missing Runner", sessionFile);
                var window = new MainWindow();

                AddSession(window, session);
                SelectSingleSession(window, session);
                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);

                var preview = (MaintenancePreview)typeof(MainWindow)
                    .GetField("_currentMaintenancePreview", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                var runner = (Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)typeof(MainWindow)
                    .GetProperty("MaintenanceRunner", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    runner(preview, Path.Combine(root, "archive"), string.Empty, CancellationToken.None));
                Assert.Equal("Maintenance executor has not been initialized.", exception.Message);
            }
            finally
            {
                DeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_preserves_nonblank_destination_rootAsync()
    {
        await RunInStaAsync(async () =>
        {
            var root = CreateTempDirectory();
            try
            {
                var sessionFile = WriteSessionJsonl(root, "session-custom-destination", "Custom Destination");
                var session = BuildIndexedSession("session-custom-destination", "Custom Destination", sessionFile);
                var requestedDestinationRoot = Path.Combine(root, "custom-archive");
                var observedDestinationRoots = new List<string>();
                var window = new MainWindow();

                MaintenanceExecutorField.SetValue(window, new MaintenanceExecutor(Path.Combine(root, "checkpoints")));
                AddSession(window, session);
                SelectSingleSession(window, session);
                SetProvider(
                    window,
                    "MaintenanceRunner",
                    ((Func<MaintenancePreview, string, string, CancellationToken, Task<MaintenanceExecutionResult>>)((_, destinationRoot, _, _) =>
                    {
                        observedDestinationRoots.Add(destinationRoot);
                        return Task.FromResult(new MaintenanceExecutionResult(false, [], Path.Combine(root, "checkpoint.json")));
                    })));

                BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
                GetNamedField<TextBox>(window, "DestinationRootTextBox").Text = requestedDestinationRoot;

                await InvokePrivateTaskAsync(window, ExecuteMaintenanceUiAsyncMethod);

                Assert.Single(observedDestinationRoots);
                Assert.Equal(requestedDestinationRoot, observedDestinationRoots[0]);
                Assert.Equal("Maintenance did not execute.", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
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
}
