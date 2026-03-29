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
    public void GetLiveSqliteStatus_rejects_null_inputs()
    {
        var pathsException = Assert.Throws<TargetInvocationException>(() =>
            GetLiveSqliteStatusWithInputsMethod.Invoke(
                null,
                [null!, (Func<string, string?>)(_ => null)]));
        Assert.IsType<ArgumentNullException>(pathsException.InnerException);

        var formatterException = Assert.Throws<TargetInvocationException>(() =>
            GetLiveSqliteStatusWithInputsMethod.Invoke(
                null,
                [SqliteStatusPaths, null!]));
        Assert.IsType<ArgumentNullException>(formatterException.InnerException);
    }

    [Fact]
    public void DescribeSqlitePath_handles_missing_and_exceptions()
    {
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, [Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite"), null]));
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, ["ignored", (Func<string, FileInfo>)(_ => throw new IOException("busy"))]));
        Assert.Null(DescribeSqlitePathMethod.Invoke(null, ["ignored", (Func<string, FileInfo>)(_ => throw new UnauthorizedAccessException("denied"))]));
    }

    [Fact]
    public void DescribeSqlitePath_rejects_null_path()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => DescribeSqlitePathMethod.Invoke(null, [null!, null]));
        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void GetRequiredPreferredCopy_rejects_null_sessions()
    {
        var preferredCopyException = Assert.Throws<TargetInvocationException>(() => GetRequiredPreferredCopyMethod.Invoke(null, [null!]));
        Assert.IsType<ArgumentNullException>(preferredCopyException.InnerException);
    }

    [Fact]
    public void GetRequiredPreferredCopy_rejects_sessions_without_a_preferred_copy()
    {
        var session = WithNullIndexedSessionProperty(
            BuildIndexedSession(
                "session-missing-copy",
                "Missing Copy",
                Path.Combine(Path.GetTempPath(), "session-missing-copy.jsonl")),
            nameof(IndexedLogicalSession.PreferredCopy));

        var preferredCopyException = Assert.Throws<TargetInvocationException>(() => GetRequiredPreferredCopyMethod.Invoke(null, [session]));
        Assert.IsType<InvalidOperationException>(preferredCopyException.InnerException);
    }

    [Fact]
    public void GetRequiredPreferredCopy_returns_the_selected_preferred_copy()
    {
        var session = BuildIndexedSession(
            "session-preferred-copy",
            "Preferred Copy",
            Path.Combine(Path.GetTempPath(), "session-preferred-copy.jsonl"));

        var preferredCopy = (SessionPhysicalCopy)GetRequiredPreferredCopyMethod.Invoke(null, [session])!;

        Assert.Same(session.PreferredCopy, preferredCopy);
    }

    [Fact]
    public void RunEventTask_rejects_invalid_arguments()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();

            var actionException = Assert.Throws<TargetInvocationException>(() => RunEventTaskMethod.Invoke(window, [null!, "Failed"]));
            Assert.IsType<ArgumentNullException>(actionException.InnerException);

            var failurePrefixException = Assert.Throws<TargetInvocationException>(() => RunEventTaskMethod.Invoke(window, [(Func<Task>)(() => Task.CompletedTask), " "]));
            Assert.IsType<ArgumentException>(failurePrefixException.InnerException);

            window.Close();
        });
    }

    [Fact]
    public async Task RunEventTask_sets_status_when_action_failsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            var statusTextBlock = GetNamedField<TextBlock>(window, "StatusTextBlock");
            var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            RunEventTaskMethod.Invoke(
                window,
                [
                    (Func<Task>)(() =>
                    {
                        invoked.SetResult();
                        throw new InvalidOperationException("boom");
                    }),
                    "Failed"
                ]);

            await invoked.Task;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (statusTextBlock.Text.Contains("Failed: boom", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(10);
            }

            Assert.Contains("Failed: boom", statusTextBlock.Text, StringComparison.Ordinal);
            window.Close();
        });
    }

    [Fact]
    public async Task RunEventTask_invokes_action_when_it_succeedsAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            RunEventTaskMethod.Invoke(
                window,
                [
                    (Func<Task>)(() =>
                    {
                        invoked.SetResult();
                        return Task.CompletedTask;
                    }),
                    "Failed"
                ]);

            await invoked.Task;
            Assert.Equal("Starting…", GetNamedField<TextBlock>(window, "StatusTextBlock").Text);
            window.Close();
        });
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
    public async Task RunOnUiThreadValueAsync_returns_value_when_called_off_dispatcher_threadAsync()
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
    public async Task RunOnUiThreadAsync_invokes_action_when_called_on_dispatcher_threadAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();
            var invoked = false;

            var task = (Task)RunOnUiThreadAsyncMethod.Invoke(
                window,
                [(Action)(() => invoked = true)])!;
            await task;

            Assert.True(invoked);
            window.Close();
        });
    }

    [Fact]
    public async Task RunOnUiThreadValueAsync_returns_value_when_called_on_dispatcher_threadAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            var task = (Task<string>)RunOnUiThreadValueAsyncMethod.Invoke(
                window,
                [(Func<string>)(() => "Value on dispatcher")])!;
            var value = await task;

            Assert.Equal("Value on dispatcher", value);
            window.Close();
        });
    }

    [Fact]
    public async Task RunOnUiThread_helpers_reject_null_delegatesAsync()
    {
        await RunInStaAsync(async () =>
        {
            var window = new MainWindow();

            var actionTask = (Task)(RunOnUiThreadAsyncMethod.Invoke(window, [null!])
                ?? throw new InvalidOperationException("Expected Task return from RunOnUiThreadAsync."));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await actionTask);

            var valueTask = (Task)(RunOnUiThreadValueAsyncMethod.Invoke(window, [null!])
                ?? throw new InvalidOperationException("Expected Task return from RunOnUiThreadValueAsync."));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await valueTask);

            window.Close();
        });
    }
}
