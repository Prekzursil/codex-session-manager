using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Indexing;
using Microsoft.Data.Sqlite;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionCatalogRepositoryFallbackTests
{
    [Fact]
    public async Task UpsertAsync_UsesIncomingMetadata_WhenReindexedDocumentAlreadyContainsValues()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var initial = CreateSession(
                "session-override",
                "Initial",
                alias: string.Empty,
                tags: [],
                notes: string.Empty);

            await repository.UpsertAsync(initial, CancellationToken.None);
            await repository.SaveMetadataAsync("session-override", "Stored alias", ["stored"], "stored note", CancellationToken.None);

            var reindexed = initial with
            {
                SearchDocument = initial.SearchDocument with
                {
                    Alias = "Incoming alias",
                    Tags = ["incoming"],
                    Notes = "incoming note"
                }
            };

            await repository.UpsertAsync(reindexed, CancellationToken.None);

            var stored = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));
            Assert.Equal("Incoming alias", stored.SearchDocument.Alias);
            Assert.Equal(["incoming"], stored.SearchDocument.Tags);
            Assert.Equal("incoming note", stored.SearchDocument.Notes);
        }
        finally
        {
            DeleteFileBestEffort(databasePath);
        }
    }

    [Fact]
    public async Task ListSessionsAsync_FallsBack_WhenSessionHasNoCopies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await ExecuteSetupSqlAsync(databasePath, CreateSessionSetupSql(
                "orphan-session",
                "Orphan",
                "orphan.jsonl"));

            var repository = new SessionCatalogRepository(databasePath);
            var session = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));

            Assert.Equal("orphan-session", session.SessionId);
            Assert.Equal("orphan.jsonl", session.PreferredCopy.FilePath);
            Assert.Single(session.PhysicalCopies);
        }
        finally
        {
            DeleteFileBestEffort(databasePath);
        }
    }

    [Fact]
    public async Task ListSessionsAsync_FallsBack_WhenPreferredPathIsMissingFromExistingCopies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await ExecuteSetupSqlAsync(
                databasePath,
                CreateSessionSetupSql(
                    "mismatch-session",
                    "Mismatch",
                    "preferred.jsonl",
                    """
                    INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
                    VALUES ('mismatch-session', 'actual.jsonl', 0, '2026-03-26T10:00:00.0000000+00:00', 7, 0);
                    """));

            var repository = new SessionCatalogRepository(databasePath);
            var session = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));

            Assert.Equal("preferred.jsonl", session.PreferredCopy.FilePath);
            Assert.Equal(SessionStoreKind.Unknown, session.PreferredCopy.StoreKind);
            Assert.Single(session.PhysicalCopies);
            Assert.Equal("actual.jsonl", session.PhysicalCopies[0].FilePath);
        }
        finally
        {
            DeleteFileBestEffort(databasePath);
        }
    }

    private static async Task<SessionCatalogRepository> CreateRepositoryAsync(string databasePath)
    {
        var repository = new SessionCatalogRepository(databasePath);
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }

    private static IndexedLogicalSession CreateSession(
        string sessionId,
        string threadName,
        string alias,
        IReadOnlyList<string> tags,
        string notes)
    {
        var searchDocument = new SessionSearchDocument
        {
            ReadableTranscript = "first transcript",
            DialogueTranscript = "first transcript",
            ToolSummary = string.Empty,
            CommandText = string.Empty,
            FilePaths = [],
            Urls = [],
            ErrorText = string.Empty,
            Alias = alias,
            Tags = tags,
            Notes = notes
        };

        var preferredCopy = new SessionPhysicalCopy(
            sessionId,
            $@"C:\Users\Prekzursil\.codex\sessions\{sessionId}.jsonl",
            SessionStoreKind.Live,
            new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1000, false));

        return new IndexedLogicalSession(sessionId, threadName, preferredCopy, [preferredCopy], searchDocument);
    }

    private static string CreateSessionSetupSql(string sessionId, string threadName, string preferredPath, string trailingSql = "")
    {
        return
            $"""
             CREATE TABLE IF NOT EXISTS sessions (
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
             CREATE TABLE IF NOT EXISTS session_copies (
                 session_id TEXT NOT NULL,
                 file_path TEXT NOT NULL,
                 store_kind INTEGER NOT NULL,
                 last_write_utc TEXT NOT NULL,
                 file_size_bytes INTEGER NOT NULL,
                 is_hot INTEGER NOT NULL,
                 PRIMARY KEY(session_id, file_path)
             );
             INSERT INTO sessions(session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes, combined_text)
             VALUES ('{sessionId}', '{threadName}', '{preferredPath}', 'transcript', 'dialogue', '', '', '', '', '', '', '', '', 'transcript');
             {trailingSql}
             """;
    }

    private static async Task ExecuteSetupSqlAsync(string databasePath, string setupSql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = setupSql;
        await command.ExecuteNonQueryAsync();
    }

    private static void DeleteFileBestEffort(string databasePath)
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
