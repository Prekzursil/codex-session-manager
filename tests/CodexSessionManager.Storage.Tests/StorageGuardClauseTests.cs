#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
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

    private static readonly MethodInfo TryGetPropertyValueMethod =
        typeof(SessionJsonlParser).GetMethod("TryGetPropertyValue", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryExtractCommandMethod =
        typeof(SessionJsonlParser).GetMethod("TryExtractCommand", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryGetTextContentMethod =
        typeof(SessionJsonlParser).GetMethod("TryGetTextContent", BindingFlags.NonPublic | BindingFlags.Static)!;

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

    private static readonly MethodInfo ReplaceCopyRowsMethod =
        typeof(SessionCatalogRepository).GetMethod("ReplaceCopyRowsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo LoadSessionsMethod =
        typeof(SessionCatalogRepository).GetMethod("LoadSessionsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteReaderMethod =
        typeof(SessionCatalogRepository).GetMethod("ExecuteReaderAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteNonQueryMethod =
        typeof(SessionCatalogRepository).GetMethod("ExecuteNonQueryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo SplitLinesMethod =
        typeof(SessionCatalogRepository).GetMethod("SplitLines", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ToFtsQueryMethod =
        typeof(SessionCatalogRepository).GetMethod("BuildFtsQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ToFtsTokenMethod =
        typeof(SessionCatalogRepository).GetMethod("ToFtsToken", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ReadRequiredStringMethod =
        typeof(SessionCatalogRepository).GetMethod("ReadRequiredString", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo OpenConnectionMethod =
        typeof(SessionCatalogRepository).GetMethod("OpenConnection", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DatabasePathField =
        typeof(SessionCatalogRepository).GetField("_databasePath", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo IsProtectedMethod =
        typeof(MaintenancePlanner).GetMethod("IsProtected", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateKnownSessionStoreMethod =
        typeof(SessionDiscoveryService).GetMethod("CreateKnownSessionStore", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo NormalizeRootPathMethod =
        typeof(SessionDiscoveryService).GetMethod("NormalizeRootPath", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public async Task Public_guard_clauses_throw_for_null_inputsAsync()
    {
        var repository = new SessionCatalogRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        var previewTarget = new SessionPhysicalCopy("session-1", Path.Combine(Path.GetTempPath(), "session-1.jsonl"), SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false));

        Assert.Throws<ArgumentException>(() => new SessionCatalogRepository(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => SessionJsonlParser.ParseAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => SessionDiscoveryService.DiscoverAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            SessionDiscoveryService.DiscoverAsync(new SessionStoreRoot[] { null! }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.UpsertAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SearchAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveMetadataAsync(null!, string.Empty, [], string.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SaveMetadataAsync("session-1", string.Empty, null!, string.Empty, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => MaintenancePlanner.CreatePreview(null!));
        Assert.Throws<ArgumentNullException>(() => MaintenancePlanner.CreatePreview(new MaintenanceRequest(MaintenanceAction.Archive, [null!], "ARCHIVE 1 FILE")));
        Assert.NotNull(previewTarget);
    }

    [Fact]
    public async Task Private_helpers_validate_selected_inputsAsync()
    {
        var emptyElement = JsonDocument.Parse("{}").RootElement.Clone();
        var sessionMetaEnvelope = JsonDocument.Parse("""{"type":"session_meta","payload":{}}""").RootElement.Clone();
        var filePaths = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        AssertInner<ArgumentNullException>(() => ParseLineMethod.Invoke(null, [sessionMetaEnvelope, null!]));
        AssertInner<ArgumentNullException>(() => ParseSessionMetadataMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseResponseItemMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseMessageMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseFunctionCallMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => ParseFunctionCallOutputMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() => TryGetStringMethod.Invoke(null, [emptyElement, null!]));
        AssertInner<ArgumentNullException>(() =>
            TryGetPropertyValueMethod.Invoke(null, [emptyElement, null!, default(JsonElement)]));
        Assert.Null(TryExtractCommandMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ExtractFilePathsAndUrlsMethod.Invoke(null, [string.Empty, null!, urls]));
        AssertInner<ArgumentNullException>(() => ExtractFilePathsAndUrlsMethod.Invoke(null, [string.Empty, filePaths, null!]));
        AssertInner<ArgumentNullException>(() => ExtractFilePathsAndUrlsMethod.Invoke(null, [null!, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)]));
        AssertInner<ArgumentNullException>(() => TryExtractExitCodeMethod.Invoke(null, [null!, 0]));
        await AssertInnerAsync<ArgumentException>(() => (Task)RefreshSearchRowMethod.Invoke(null, [connection, null!, CancellationToken.None])!);
        AssertInner<ArgumentNullException>(() => ToFtsQueryMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ToFtsTokenMethod.Invoke(null, [null!]));
        AssertInner<ArgumentNullException>(() => ReadRequiredStringMethod.Invoke(null, [null!, 0]));
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task<SqliteDataReader>)ExecuteReaderMethod.Invoke(null, [null!, CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task)ExecuteNonQueryMethod.Invoke(null, [null!, CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task)ReplaceCopyRowsMethod.Invoke(
                null,
                [null!, "session-1", Array.Empty<SessionPhysicalCopy>(), CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task)ReplaceCopyRowsMethod.Invoke(null, [connection, "session-1", null!, CancellationToken.None])!);
        var splitLines = (IReadOnlyList<string>)SplitLinesMethod.Invoke(null, [null!])!;
        Assert.Empty(splitLines);
        AssertInner<ArgumentNullException>(() => CreateKnownSessionStoreMethod.Invoke(null, [null!]));
        AssertInner<ArgumentException>(
            () => CreateKnownSessionStoreMethod.Invoke(null, [new SessionStoreRoot(" ", SessionStoreKind.Live)]));
        AssertInner<ArgumentNullException>(() => NormalizeRootPathMethod.Invoke(null, [null!]));
    }

    [Fact]
    public void ParseLine_ignores_payloadless_entries_and_invalid_timestamps()
    {
        var sessionMetaWithoutPayload = JsonDocument.Parse("""{"type":"session_meta"}""").RootElement.Clone();
        var responseItemWithoutPayload = JsonDocument.Parse("""{"type":"response_item"}""").RootElement.Clone();
        var invalidTimestampPayload = JsonDocument.Parse("""{"id":"session-1","timestamp":"not-a-date"}""").RootElement.Clone();
        var state = Activator.CreateInstance(ParseStateType)!;

        ParseLineMethod.Invoke(null, [sessionMetaWithoutPayload, state]);
        ParseLineMethod.Invoke(null, [responseItemWithoutPayload, state]);
        ParseSessionMetadataMethod.Invoke(null, [invalidTimestampPayload, state]);
        ParseSessionMetadataMethod.Invoke(null, [JsonDocument.Parse("""{"id":"session-2"}""").RootElement.Clone(), state]);
        ParseSessionMetadataMethod.Invoke(null, [JsonDocument.Parse("""{"timestamp":null}""").RootElement.Clone(), state]);

        var startedAt = (DateTimeOffset)ParseStateType.GetProperty("StartedAtUtc")!.GetValue(state)!;
        Assert.Equal(DateTimeOffset.MinValue, startedAt);

        ParseStateType.GetProperty("StartedAtUtc")!.SetValue(state, new DateTimeOffset(2026, 3, 27, 2, 0, 0, TimeSpan.Zero));
        ParseSessionMetadataMethod.Invoke(null, [JsonDocument.Parse("""{"timestamp":"2026-03-27T04:00:00Z"}""").RootElement.Clone(), state]);
        Assert.Equal(new DateTimeOffset(2026, 3, 27, 2, 0, 0, TimeSpan.Zero), (DateTimeOffset)ParseStateType.GetProperty("StartedAtUtc")!.GetValue(state)!);
    }

    [Fact]
    public void TryGetTextContent_returns_false_when_text_property_is_missing()
    {
        var contentItem = JsonDocument.Parse("""{"type":"output_text"}""").RootElement.Clone();
        object?[] args = [contentItem, string.Empty];

        var result = (bool)TryGetTextContentMethod.Invoke(null, args)!;

        Assert.False(result);
        Assert.Equal(string.Empty, args[1]);
    }

    [Fact]
    public void TryGetString_returns_null_for_non_object_elements()
    {
        var scalarElement = JsonDocument.Parse("""1""").RootElement.Clone();

        var result = TryGetStringMethod.Invoke(null, [scalarElement, "payload"]);

        Assert.Null(result);
    }

    [Fact]
    public async Task Repository_private_branches_reject_missing_required_session_membersAsync()
    {
        var session = new IndexedLogicalSession(
            "session-branch",
            "Thread",
            new SessionPhysicalCopy("session-branch", Path.Combine(Path.GetTempPath(), "session-branch.jsonl"), SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false)),
            [],
            new SessionSearchDocument());

        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new SessionCatalogRepository(databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PreferredCopy)), CancellationToken.None));

        await repository.UpsertAsync(WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.PhysicalCopies)), CancellationToken.None);
        var stored = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));
        Assert.Single(stored.PhysicalCopies);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task<SessionSearchDocument>)MergeExistingMetadataMethod.Invoke(
                null,
                [null!, session, CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task<SessionSearchDocument>)MergeExistingMetadataMethod.Invoke(
                null,
                [connection, null!, CancellationToken.None])!);
        await AssertInnerAsync<InvalidOperationException>(() => (Task<SessionSearchDocument>)MergeExistingMetadataMethod.Invoke(null, [connection, WithNullIndexedSessionProperty(session, nameof(IndexedLogicalSession.SearchDocument)), CancellationToken.None])!);
        await AssertInnerAsync<ArgumentNullException>(() =>
            (Task)ReplaceCopyRowsMethod.Invoke(
                null,
                [connection, session.SessionId, new SessionPhysicalCopy[] { null! }, CancellationToken.None])!);

        DatabasePathField.SetValue(repository, string.Empty);
        AssertInner<InvalidOperationException>(() => OpenConnectionMethod.Invoke(repository, [CancellationToken.None]));
    }

    [Fact]
    public async Task LoadSessionsAsync_treats_null_copy_entries_as_missingAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE sessions (
                session_id TEXT PRIMARY KEY,
                thread_name TEXT NOT NULL,
                preferred_path TEXT NOT NULL,
                readable_transcript TEXT NOT NULL,
                dialogue_transcript TEXT NOT NULL,
                tool_summary TEXT NOT NULL,
                command_text TEXT NOT NULL,
                file_paths TEXT NOT NULL,
                urls TEXT NOT NULL,
                error_text TEXT NOT NULL,
                alias TEXT NOT NULL,
                tags TEXT NOT NULL,
                notes TEXT NOT NULL
            );
            INSERT INTO sessions(session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes)
            VALUES ('session-null-copies', 'Thread', 'preferred.jsonl', 'readable', 'dialogue', '', '', '', '', '', '', '', '');
            """;
        await command.ExecuteNonQueryAsync();

        var copiesBySession = new Dictionary<string, List<SessionPhysicalCopy>>(StringComparer.Ordinal)
        {
            ["session-null-copies"] = null!,
        };

        var sessions = (IReadOnlyList<IndexedLogicalSession>)await ((Task<IReadOnlyList<IndexedLogicalSession>>)LoadSessionsMethod.Invoke(
            null,
            [connection, copiesBySession, CancellationToken.None])!);
        var session = Assert.Single(sessions);

        Assert.Equal("preferred.jsonl", session.PreferredCopy.FilePath);
        Assert.Single(session.PhysicalCopies);
        Assert.Equal("preferred.jsonl", session.PhysicalCopies[0].FilePath);
    }

    private static IndexedLogicalSession WithNullIndexedSessionProperty(IndexedLogicalSession session, string propertyName)
    {
        var clone = session with { };
        typeof(IndexedLogicalSession).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(clone, null);
        return clone;
    }

    [Fact]
    public void MaintenancePlanner_handles_null_target_collections_and_private_null_candidate_guard()
    {
        var request = new MaintenanceRequest(MaintenanceAction.Archive, [new SessionPhysicalCopy("session", Path.Combine(Path.GetTempPath(), "session.jsonl"), SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false))], "ARCHIVE");
        typeof(MaintenanceRequest).GetField("<Targets>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(request, null);

        var preview = MaintenancePlanner.CreatePreview(request);

        Assert.Empty(preview.AllowedTargets);
        Assert.Empty(preview.BlockedTargets);
        Assert.Empty(preview.Warnings);
        AssertInner<ArgumentNullException>(() => IsProtectedMethod.Invoke(null, [null!]));
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
