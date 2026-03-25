using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexSessionManager.App;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Storage.Discovery;
using Xunit;

namespace CodexSessionManager.App.Tests;

public sealed class MainWindowCoverageTests
{
    private static readonly MethodInfo BuildKnownStoresMethod =
        typeof(MainWindow).GetMethod("BuildKnownStores", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo GetLiveSqliteStatusMethod =
        typeof(MainWindow).GetMethod("GetLiveSqliteStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo LoadSessionsFromCatalogAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSessionsFromCatalogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RefreshAsyncMethod =
        typeof(MainWindow).GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SessionsSelectionChangedMethod =
        typeof(MainWindow).GetMethod(
            "SessionsListBox_OnSelectionChanged",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SearchTextChangedMethod =
        typeof(MainWindow).GetMethod(
            "SearchTextBox_OnTextChanged",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SaveMetadataMethod =
        typeof(MainWindow).GetMethod(
            "SaveMetadataButton_OnClick",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

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

    private static readonly MethodInfo ExecuteMaintenanceMethod =
        typeof(MainWindow).GetMethod("ExecuteMaintenanceButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static IReadOnlyList<KnownSessionStore> InvokeBuildKnownStores(bool deepScan) =>
        (IReadOnlyList<KnownSessionStore>)BuildKnownStoresMethod.Invoke(null, [deepScan])!;

    private static Task InvokePrivateTask(object instance, MethodInfo method, params object?[] args) =>
        (Task)method.Invoke(instance, args)!;

    private static T GetNamedField<T>(MainWindow window, string name) where T : class =>
        (typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as T)
        ?? throw new InvalidOperationException($"Field '{name}' was not found.");

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

    [Fact]
    public void Constructor_initializes_core_bindings()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var sessionsListBox = GetNamedField<ListBox>(window, "SessionsListBox");
            var maintenanceActionComboBox = GetNamedField<ComboBox>(window, "MaintenanceActionComboBox");
            var statusTextBlock = GetNamedField<TextBlock>(window, "StatusTextBlock");

            Assert.NotNull(sessionsListBox.ItemsSource);
            Assert.Equal(MaintenanceAction.Archive, maintenanceActionComboBox.SelectedItem);
            Assert.Equal("Starting…", statusTextBlock.Text);
        });
    }

    [Fact]
    public void BuildKnownStores_matches_locator_for_non_deep_scan()
    {
        var expected = KnownStoreLocator.GetKnownStores(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));

        var actual = InvokeBuildKnownStores(deepScan: false);

        Assert.Equal(expected.Select(store => store.SessionsPath), actual.Select(store => store.SessionsPath));
    }

    [Fact]
    public void GetLiveSqliteStatus_returns_non_empty_status()
    {
        var value = (string)GetLiveSqliteStatusMethod.Invoke(null, [])!;

        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public async Task Null_guard_paths_return_without_throwing()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var sessionsListBox = GetNamedField<ListBox>(window, "SessionsListBox");
            var searchTextBox = GetNamedField<TextBox>(window, "SearchTextBox");

            InvokePrivateTask(window, LoadSessionsFromCatalogAsyncMethod).GetAwaiter().GetResult();
            InvokePrivateTask(window, RefreshAsyncMethod, false).GetAwaiter().GetResult();

            SessionsSelectionChangedMethod.Invoke(
                window,
                [
                    sessionsListBox,
                    new SelectionChangedEventArgs(
                        Selector.SelectionChangedEvent,
                        Array.Empty<object>(),
                        Array.Empty<object>())
                ]);

            SearchTextChangedMethod.Invoke(
                window,
                [
                    searchTextBox,
                    new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None)
                ]);

            SaveMetadataMethod.Invoke(window, [window, new RoutedEventArgs()]);
            OpenFolderMethod.Invoke(window, [window, new RoutedEventArgs()]);
            OpenRawMethod.Invoke(window, [window, new RoutedEventArgs()]);
            CopyPathMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ResumeMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ExportMethod.Invoke(window, [window, new RoutedEventArgs()]);
            BuildPreviewMethod.Invoke(window, [window, new RoutedEventArgs()]);
            ExecuteMaintenanceMethod.Invoke(window, [window, new RoutedEventArgs()]);
        });
    }
}
