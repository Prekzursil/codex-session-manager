using System.Reflection;
using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;
using Microsoft.Data.Sqlite;

namespace CodexSessionManager.Storage.Tests;

public sealed class StorageGuardClauseTests
{
    private static readonly Type ParseStateType =
        typeof(SessionJsonlParser).GetNestedType("ParseState", BindingFlags.NonPublic)!;

    private static readonly MethodInfo ParseLineMethod =
        typeof(SessionJsonlParser).GetMethod("ParseLine", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ParseSessionMetadataMethod =
        typeof(SessionJsonlParser).GetMethod("ParseSessionMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ParseResponseItemMethod =
        typeof(SessionJsonlParser).GetMethod("ParseResponseItem", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ParseMessageMethod =
        typeof(SessionJsonlParser).GetMethod("ParseMessage", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ParseFunctionCallMethod =
        typeof(SessionJsonlParser).GetMethod("ParseFunctionCall", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ParseFunctionCallOutputMethod =
        typeof(SessionJsonlParser).GetMethod("ParseFunctionCallOutput", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryGetStringMethod =
        typeof(SessionJsonlParser).GetMethod("TryGetString", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryExtractCommandMethod =
        typeof(SessionJsonlParser).GetMethod("TryExtractCommand", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExtractFilePathsAndUrlsMethod =
        typeof(SessionJsonlParser).GetMethod("ExtractFilePathsAndUrls", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryExtractExitCodeMethod =
        typeof(SessionJsonlParser).GetMethod("TryExtractExitCode", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MergeExistingMetadataMethod =
        typeof(SessionCatalogRepository).GetMethod("MergeExistingMetadataAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo RefreshSearchIndexMethod =
        typeof(SessionCatalogRepository).GetMethod("RefreshSearchIndexAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo RefreshSearchRowMethod =
        typeof(SessionCatalogRepository).GetMethod("RefreshSearchRowAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SplitLinesMethod =
        typeof(SessionCatalogRepository).GetMethod("SplitLines", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ToFtsQueryMethod =
        typeof(SessionCatalogRepository).GetMethod("ToFtsQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ToFtsTokenMethod =
        typeof(SessionCatalogRepository).GetMethod("ToFtsToken", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public async Task Public_guard_clauses_throw_for_null_inputs()
    {
        var repository = new SessionCatalogRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        var previewTarget = new SessionPhysicalCopy("session-1", @"C:\tmp\session-1.jsonl", SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false));

        await Assert.ThrowsAsync<ArgumentException>(() => SessionJsonlParser.ParseAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => SessionDiscoveryService.DiscoverAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.UpsertAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SearchAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveMetadataAsync(null!, string.Empty, [], string.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SaveMetadataAsync("session-1", string.Empty, null!, string.Empty, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => MaintenancePlanner.CreatePreview(null!));
        Assert.Throws<ArgumentNullException>(() => MaintenancePlanner.CreatePreview(new MaintenanceRequest(MaintenanceAction.Archive, [null!], "ARCHIVE 1 FILE")));
        Assert.NotNull(previewTarget);
    }

    [Fact]
    public async Task Private_guard_clauses_throw_for_null_inputs()
    {
        var emptyElement = JsonDocument.Parse("{}").RootElement.Clone();
        var state = Activator.CreateInstance(ParseStateType)!;
        var filePaths = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var repositorySession = new IndexedLogicalSession(
            "session-1",
            "Thread",
            new SessionPhysicalCopy("session-1", @"C:\tmp\session-1.jsonl", SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false)),
            [],
            new SessionSearchDocument());

        AssertInner<ArgumentNullException>(() => ParseLineMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseSessionMetadataMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseResponseItemMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseMessageMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseFunctionCallMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseFunctionCallOutputMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => TryGetStringMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => TryExtractCommandMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ExtractFilePathsAndUrlsMethod.Invoke(null, [string.Empty, null!, urls]));
        AssertInner<ArgumentNullException>(() => ExtractFilePathsAndUrlsMethod.Invoke(null, [string.Empty, filePaths, null!]));
        AssertInner<ArgumentNullException>(() => TryExtractExitCodeMethod.Invoke(null, [null!, 0]));

        await AssertInnerAsync<ArgumentNullException>(() => (Task<SessionSearchDocument>)MergeExistingMetadataMethod.Invoke(null, [null!, repositorySession, CancellationToken.None])!);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await AssertInnerAsync<ArgumentNullException>(() => (Task<SessionSearchDocument>)MergeExistingMetadataMethod.Invoke(null, [connection, null!, CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() => (Task)RefreshSearchIndexMethod.Invoke(null, [null!, CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() => (Task)RefreshSearchRowMethod.Invoke(null, [null!, "session-1", CancellationToken.None])!);
        await AssertInnerAsync<ArgumentException>(() => (Task)RefreshSearchRowMethod.Invoke(null, [connection, null!, CancellationToken.None])!);
        AssertInner<ArgumentNullException>(() => SplitLinesMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ToFtsQueryMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ToFtsTokenMethod.Invoke(null, [null!]));
    }

    private static void AssertInner<TException>(Action action)
        where TException : Exception
    {
        var exception = Assert.Throws<TargetInvocationException>(action);
        Assert.IsType<TException>(exception.InnerException);
    }

    private static async Task AssertInnerAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        await Assert.ThrowsAsync<TException>(action);
    }
}
