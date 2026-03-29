#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Indexing;

namespace CodexSessionManager.Storage.Tests;

[Collection("CurrentDirectorySensitive")]
public sealed class SessionCatalogRepositoryTests
{
    [Fact]
    public async Task SearchAsync_FindsTranscript_Alias_AndCommandTextAsync()
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
    public async Task SearchAsync_MatchesPhraseTokens_OutOfOriginalOrderAsync()
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
    public async Task SearchAsync_ReturnsEmptyList_ForWhitespaceQueryAsync()
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
    public async Task InitializeAsync_BackfillsSearchIndex_ForPreexistingSessionRowsAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
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
    public async Task SaveMetadataAsync_PersistsAliasTagsAndNotes_AndMakesThemSearchableAsync()
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
    public async Task SearchAsync_ReturnsSnippet_WhenMatchOccursOutsideThreadNameAsync()
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
    public async Task UpsertAsync_PreservesExistingMetadata_WhenReindexingSameSessionAsync()
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
    public async Task InitializeAsync_Supports_database_path_without_directoryAsync()
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
    public async Task ListSessionsAsync_ReturnsPersistedMetadata_WhenStoredMetadataExistsAsync()
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

    [Fact]
    public async Task UpsertAsync_And_ListSessionsAsync_PreserveHotCopyStateAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);
            var hotCopy = new SessionPhysicalCopy(
                "session-hot-copy",
                @"C:\Users\Prekzursil\.codex\sessions\session-hot-copy.jsonl",
                SessionStoreKind.Live,
                new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1000, true));

            var session = new IndexedLogicalSession(
                "session-hot-copy",
                "Hot Copy",
                hotCopy,
                [hotCopy],
                new SessionSearchDocument
                {
                    ReadableTranscript = "hot transcript",
                    DialogueTranscript = "hot transcript",
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

            var stored = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));

            Assert.True(stored.PreferredCopy.IsHot);
            Assert.True(stored.PhysicalCopies.Single().IsHot);
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

