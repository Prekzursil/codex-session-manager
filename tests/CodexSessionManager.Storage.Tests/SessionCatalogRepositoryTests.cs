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

            var session = CreateIndexedSession(
                "session-1",
                "Renderer work",
                new SessionSearchDocument
                {
                    ReadableTranscript = "User asked to inspect renderer logic",
                    DialogueTranscript = "inspect renderer logic",
                    ToolSummary = "Ran exec_command: rg -n session renderer",
                    CommandText = "rg -n session renderer",
                    FilePaths = [@"C:\repo\renderer.mjs"],
                    Urls = [],
                    ErrorText = "",
                    Alias = "Important renderer session",
                    Tags = ["renderer", "search"],
                    Notes = "Keeps the parser behavior"
                },
                new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero));

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

            var session = CreateIndexedSession(
                "session-fts",
                "Renderer work",
                new SessionSearchDocument
                {
                    ReadableTranscript = "inspect renderer logic",
                    DialogueTranscript = "inspect renderer logic",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "",
                    Tags = [],
                    Notes = ""
                });

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

            var session = CreateIndexedSession(
                "session-2",
                "Maintenance work",
                new SessionSearchDocument
                {
                    ReadableTranscript = "transcript",
                    DialogueTranscript = "dialogue",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "",
                    Tags = [],
                    Notes = ""
                },
                new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero));

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
    public async Task SearchAsync_ReturnsSnippet_WhenMatchOccursOutsideThreadName()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = CreateIndexedSession(
                "session-empty-snippet",
                "Thread heading",
                new SessionSearchDocument
                {
                    ReadableTranscript = "readable",
                    DialogueTranscript = "dialogue",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "unique alias marker",
                    Tags = [],
                    Notes = ""
                });

            await repository.UpsertAsync(session, CancellationToken.None);

            var hit = Assert.Single(await repository.SearchAsync("unique alias marker", CancellationToken.None));
            Assert.Contains("unique", hit.Snippet, StringComparison.OrdinalIgnoreCase);
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
    public async Task UpsertAsync_PreservesExistingMetadata_WhenReindexingSameSession()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var initial = CreateIndexedSession(
                "session-3",
                "Initial",
                new SessionSearchDocument
                {
                    ReadableTranscript = "first transcript",
                    DialogueTranscript = "first transcript",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "",
                    Tags = [],
                    Notes = ""
                },
                fileSizeBytes: 100);

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
    public async Task UpsertAsync_UsesIncomingMetadata_WhenReindexedDocumentAlreadyContainsValues()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var initial = CreateIndexedSession(
                "session-override",
                "Initial",
                new SessionSearchDocument
                {
                    ReadableTranscript = "first transcript",
                    DialogueTranscript = "first transcript",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "",
                    Tags = [],
                    Notes = ""
                });

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
    public async Task ListSessionsAsync_FallsBack_WhenSessionHasNoCopies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var setupSql =
                    """
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
                    VALUES ('orphan-session', 'Orphan', 'orphan.jsonl', 'transcript', 'dialogue', '', '', '', '', '', '', '', '', 'transcript');
                    """;
                await using var command = connection.CreateCommand();
                command.CommandText = setupSql;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SessionCatalogRepository(databasePath);
            var listed = await repository.ListSessionsAsync(CancellationToken.None);

            var session = Assert.Single(listed);
            Assert.Equal("orphan-session", session.SessionId);
            Assert.Equal("orphan.jsonl", session.PreferredCopy.FilePath);
            Assert.Single(session.PhysicalCopies);
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
    public async Task ListSessionsAsync_FallsBack_WhenPreferredPathIsMissingFromExistingCopies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var setupSql =
                    """
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
                    VALUES ('mismatch-session', 'Mismatch', 'preferred.jsonl', 'transcript', 'dialogue', '', '', '', '', '', '', '', '', 'transcript');
                    INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
                    VALUES ('mismatch-session', 'actual.jsonl', 0, '2026-03-26T10:00:00.0000000+00:00', 7, 0);
                    """;
                await using var command = connection.CreateCommand();
                command.CommandText = setupSql;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SessionCatalogRepository(databasePath);
            var session = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));

            Assert.Equal("preferred.jsonl", session.PreferredCopy.FilePath);
            Assert.Equal(SessionStoreKind.Unknown, session.PreferredCopy.StoreKind);
            Assert.Single(session.PhysicalCopies);
            Assert.Equal("actual.jsonl", session.PhysicalCopies[0].FilePath);
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
    public async Task InitializeAsync_Supports_database_path_without_directory()
    {
        var databasePath = $"{Guid.NewGuid():N}.catalog.db";
        var resolvedDatabasePath = Path.GetFullPath(databasePath);

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);
            Assert.Empty(await repository.ListSessionsAsync(CancellationToken.None));
            Assert.True(File.Exists(resolvedDatabasePath));
        }
        finally
        {
            if (File.Exists(resolvedDatabasePath))
            {
                try
                {
                    File.Delete(resolvedDatabasePath);
                }
                catch (IOException)
                {
                    // best-effort cleanup for Windows temp SQLite files
                }
            }
        }
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsPersistedMetadata_WhenStoredMetadataExists()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = CreateIndexedSession(
                "session-preserve-metadata",
                "Preserve Metadata",
                new SessionSearchDocument
                {
                    ReadableTranscript = "readable",
                    DialogueTranscript = "dialogue",
                    ToolSummary = "",
                    CommandText = "",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "inline alias",
                    Tags = ["inline-tag"],
                    Notes = "inline note"
                });

            await repository.UpsertAsync(session, CancellationToken.None);
            await repository.SaveMetadataAsync("session-preserve-metadata", "stored alias", ["stored-tag"], "stored note", CancellationToken.None);

            var listed = await repository.ListSessionsAsync(CancellationToken.None);
            var stored = Assert.Single(listed);
            Assert.Equal("stored alias", stored.SearchDocument.Alias);
            Assert.Equal(["stored-tag"], stored.SearchDocument.Tags);
            Assert.Equal("stored note", stored.SearchDocument.Notes);
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

    private static IndexedLogicalSession CreateIndexedSession(
        string sessionId,
        string threadName,
        SessionSearchDocument searchDocument,
        DateTimeOffset? lastWriteTimeUtc = null,
        long fileSizeBytes = 1000)
    {
        var preferredCopy = CreateLiveCopy(
            sessionId,
            $@"C:\Users\Prekzursil\.codex\sessions\{sessionId}.jsonl",
            lastWriteTimeUtc ?? DateTimeOffset.UtcNow,
            fileSizeBytes);

        return new IndexedLogicalSession(
            sessionId,
            threadName,
            preferredCopy,
            [preferredCopy],
            searchDocument);
    }

    private static SessionPhysicalCopy CreateLiveCopy(
        string sessionId,
        string filePath,
        DateTimeOffset lastWriteTimeUtc,
        long fileSizeBytes)
    {
        return new SessionPhysicalCopy(
            sessionId,
            filePath,
            SessionStoreKind.Live,
            new SessionPhysicalCopyState(lastWriteTimeUtc, fileSizeBytes, false));
    }
}

