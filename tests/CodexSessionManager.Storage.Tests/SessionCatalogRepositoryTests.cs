using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Indexing;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionCatalogRepositoryTests
{
    [Fact]
    public async Task SearchAsync_FindsTranscript_Alias_AndCommandText()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = new IndexedLogicalSession(
                SessionId: "session-1",
                ThreadName: "Renderer work",
                PreferredCopy: new SessionPhysicalCopy(
                    "session-1",
                    @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl",
                    SessionStoreKind.Live,
                    new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero),
                    1000,
                    false),
                PhysicalCopies:
                [
                    new SessionPhysicalCopy(
                        "session-1",
                        @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl",
                        SessionStoreKind.Live,
                        new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero),
                        1000,
                        false)
                ],
                SearchDocument: new SessionSearchDocument(
                    ReadableTranscript: "User asked to inspect renderer logic",
                    DialogueTranscript: "inspect renderer logic",
                    ToolSummary: "Ran exec_command: rg -n session renderer",
                    CommandText: "rg -n session renderer",
                    FilePaths: [@"C:\repo\renderer.mjs"],
                    Urls: [],
                    ErrorText: "",
                    Alias: "Important renderer session",
                    Tags: ["renderer", "search"],
                    Notes: "Keeps the parser behavior"));

            await repository.UpsertAsync(session, CancellationToken.None);

            var transcriptHits = await repository.SearchAsync("inspect renderer", CancellationToken.None);
            var aliasHits = await repository.SearchAsync("important renderer session", CancellationToken.None);
            var commandHits = await repository.SearchAsync("rg -n session renderer", CancellationToken.None);

            Assert.Contains(transcriptHits, hit => hit.SessionId == "session-1");
            Assert.Contains(aliasHits, hit => hit.SessionId == "session-1");
            Assert.Contains(commandHits, hit => hit.SessionId == "session-1");
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // best-effort cleanup for Windows temp SQLite files
            }
        }
    }

    [Fact]
    public async Task SearchAsync_MatchesPhraseTokens_OutOfOriginalOrder()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = new IndexedLogicalSession(
                "session-fts",
                "Renderer work",
                new SessionPhysicalCopy("session-fts", @"C:\Users\Prekzursil\.codex\sessions\fts.jsonl", SessionStoreKind.Live, DateTimeOffset.UtcNow, 1000, false),
                [new SessionPhysicalCopy("session-fts", @"C:\Users\Prekzursil\.codex\sessions\fts.jsonl", SessionStoreKind.Live, DateTimeOffset.UtcNow, 1000, false)],
                new SessionSearchDocument(
                    "inspect renderer logic",
                    "inspect renderer logic",
                    "",
                    "",
                    [],
                    [],
                    "",
                    "",
                    [],
                    ""));

            await repository.UpsertAsync(session, CancellationToken.None);

            var hits = await repository.SearchAsync("renderer inspect", CancellationToken.None);

            Assert.Contains(hits, hit => hit.SessionId == "session-fts");
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // best-effort cleanup for Windows temp SQLite files
            }
        }
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_ForWhitespaceQuery()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var hits = await repository.SearchAsync("   ", CancellationToken.None);

            Assert.Empty(hits);
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // best-effort cleanup for Windows temp SQLite files
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_BackfillsSearchIndex_ForPreexistingSessionRows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var setupSql =
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
                        notes TEXT NOT NULL,
                        combined_text TEXT NOT NULL
                    );
                    INSERT INTO sessions(session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes, combined_text)
                    VALUES ('legacy-session', 'Legacy', 'C:\\legacy.jsonl', 'inspect renderer', 'inspect renderer', '', '', '', '', '', '', '', '', 'inspect renderer');
                    """;
                await using var command = connection.CreateCommand();
                command.CommandText = setupSql;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);
            var hits = await repository.SearchAsync("renderer", CancellationToken.None);

            Assert.Contains(hits, hit => hit.SessionId == "legacy-session");
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // best-effort cleanup for Windows temp SQLite files
            }
        }
    }

    [Fact]
    public async Task SaveMetadataAsync_PersistsAliasTagsAndNotes_AndMakesThemSearchable()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = new IndexedLogicalSession(
                "session-2",
                "Maintenance work",
                new SessionPhysicalCopy(
                    "session-2",
                    @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-2.jsonl",
                    SessionStoreKind.Live,
                    new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero),
                    1000,
                    false),
                [
                    new SessionPhysicalCopy(
                        "session-2",
                        @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-2.jsonl",
                        SessionStoreKind.Live,
                        new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero),
                        1000,
                        false)
                ],
                new SessionSearchDocument("transcript", "dialogue", "", "", [], [], "", "", [], ""));

            await repository.UpsertAsync(session, CancellationToken.None);
            await repository.SaveMetadataAsync("session-2", "Archive candidate", ["cleanup", "backup"], "Needs review", CancellationToken.None);

            var hits = await repository.SearchAsync("archive candidate", CancellationToken.None);
            var listed = await repository.ListSessionsAsync(CancellationToken.None);

            Assert.Contains(hits, hit => hit.SessionId == "session-2");
            Assert.Contains(listed, item => item.SessionId == "session-2" && item.SearchDocument.Alias == "Archive candidate");
            Assert.Contains(listed.Single(item => item.SessionId == "session-2").SearchDocument.Tags, tag => tag == "cleanup");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task UpsertAsync_PreservesExistingMetadata_WhenReindexingSameSession()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var initial = new IndexedLogicalSession(
                "session-3",
                "Initial",
                new SessionPhysicalCopy("session-3", @"C:\Users\Prekzursil\.codex\sessions\s3.jsonl", SessionStoreKind.Live, DateTimeOffset.UtcNow, 100, false),
                [new SessionPhysicalCopy("session-3", @"C:\Users\Prekzursil\.codex\sessions\s3.jsonl", SessionStoreKind.Live, DateTimeOffset.UtcNow, 100, false)],
                new SessionSearchDocument("first transcript", "first transcript", "", "", [], [], "", "", [], ""));

            await repository.UpsertAsync(initial, CancellationToken.None);
            await repository.SaveMetadataAsync("session-3", "Saved alias", ["pinned"], "saved note", CancellationToken.None);

            var reindexed = initial with
            {
                ThreadName = "Updated thread",
                SearchDocument = initial.SearchDocument with { ReadableTranscript = "updated transcript" }
            };

            await repository.UpsertAsync(reindexed, CancellationToken.None);

            var listed = await repository.ListSessionsAsync(CancellationToken.None);
            var stored = Assert.Single(listed);
            Assert.Equal("Saved alias", stored.SearchDocument.Alias);
            Assert.Contains(stored.SearchDocument.Tags, tag => tag == "pinned");
            Assert.Contains("saved note", stored.SearchDocument.Notes, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
