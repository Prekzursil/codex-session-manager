#pragma warning disable S3990 // Codacy false positive: the assembly already declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using CodexSessionManager.App;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.App.Tests;

[SuppressMessage("Compatibility", "S3990", Justification = "The assembly already declares CLSCompliant(true); this file-level report is a persistent analyzer false positive.")]
[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
    private static readonly MethodInfo BuildKnownStoresMethod =
        typeof(MainWindow).GetMethod("BuildKnownStores", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo StartExternalProcessMethod =
        typeof(MainWindow).GetMethod("StartExternalProcess", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo NormalizeAllowedProcessFileNameMethod =
        typeof(MainWindow).GetMethod("NormalizeAllowedProcessFileName", BindingFlags.NonPublic | BindingFlags.Static)!;

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
        typeof(MainWindow).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
            .Single(method =>
            {
                if (method.Name != "DescribeSqlitePath")
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(Func<string, FileInfo>);
            });

    private static readonly MethodInfo DescribeSqlitePathSingleArgumentMethod =
        typeof(MainWindow).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
            .Single(method =>
            {
                if (method.Name != "DescribeSqlitePath")
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType == typeof(string);
            });

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

    private static readonly MethodInfo RunEventTaskMethod =
        typeof(MainWindow).GetMethod("RunEventTask", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LoadSelectedSessionAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSelectedSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo PopulateSelectedSessionHeaderAsyncMethod =
        typeof(MainWindow).GetMethod("PopulateSelectedSessionHeaderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo LoadSelectedSessionBodyAsyncMethod =
        typeof(MainWindow).GetMethod("LoadSelectedSessionBodyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SearchSessionsAsyncMethod =
        typeof(MainWindow).GetMethod("SearchSessionsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ReloadSessionsForSearchAsyncMethod =
        typeof(MainWindow).GetMethod("ReloadSessionsForSearchAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ApplySearchResultsAsyncMethod =
        typeof(MainWindow).GetMethod("ApplySearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SaveSelectedMetadataAsyncMethod =
        typeof(MainWindow).GetMethod("SaveSelectedMetadataAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo BeginSearchTokenMethod =
        typeof(MainWindow).GetMethod("BeginSearchToken", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ReleaseSearchCancellationStateMethod =
        typeof(MainWindow).GetMethod("ReleaseSearchCancellationState", BindingFlags.NonPublic | BindingFlags.Instance)!;

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

    private static readonly MethodInfo GetSelectedSessionsMethod =
        typeof(MainWindow).GetMethod("GetSelectedSessions", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SessionsListSelectionChangedMethod =
        typeof(MainWindow).GetMethod("SessionsListBox_OnSelectionChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SearchTextChangedMethod =
        typeof(MainWindow).GetMethod("SearchTextBox_OnTextChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SaveMetadataButtonMethod =
        typeof(MainWindow).GetMethod("SaveMetadataButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RefreshButtonMethod =
        typeof(MainWindow).GetMethod("RefreshButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo DeepScanButtonMethod =
        typeof(MainWindow).GetMethod("DeepScanButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExecuteMaintenanceButtonMethod =
        typeof(MainWindow).GetMethod("ExecuteMaintenanceButton_OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo GetRequiredPreferredCopyMethod =
        typeof(MainWindow).GetMethod("GetRequiredPreferredCopy", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo SessionsField =
        typeof(MainWindow).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo RepositoryField =
        typeof(MainWindow).GetField("_repository", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo WorkspaceIndexerField =
        typeof(MainWindow).GetField("_workspaceIndexer", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo MaintenanceExecutorField =
        typeof(MainWindow).GetField("_maintenanceExecutor", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly PropertyInfo CurrentSearchCancellationTokenSourceProperty =
        typeof(MainWindow).GetProperty("CurrentSearchCancellationTokenSource", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly string[] SqliteStatusPaths = ["first", "second"];
}
