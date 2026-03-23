using CodexSessionManager.Core.Sessions;
using Microsoft.Data.Sqlite;

namespace CodexSessionManager.Storage.Indexing;

public sealed class SessionCatalogRepository
{
    private readonly string _databasePath;

    public SessionCatalogRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var effectiveSearchDocument = await MergeExistingMetadataAsync(connection, session, cancellationToken);

        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText =
                """
                INSERT INTO sessions(session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes, combined_text)
                VALUES ($sessionId, $threadName, $preferredPath, $readableTranscript, $dialogueTranscript, $toolSummary, $commandText, $filePaths, $urls, $errorText, $alias, $tags, $notes, $combinedText)
                ON CONFLICT(session_id) DO UPDATE SET
                    thread_name = excluded.thread_name,
                    preferred_path = excluded.preferred_path,
                    readable_transcript = excluded.readable_transcript,
                    dialogue_transcript = excluded.dialogue_transcript,
                    tool_summary = excluded.tool_summary,
                    command_text = excluded.command_text,
                    file_paths = excluded.file_paths,
                    urls = excluded.urls,
                    error_text = excluded.error_text,
                    alias = excluded.alias,
                    tags = excluded.tags,
                    notes = excluded.notes,
                    combined_text = excluded.combined_text;
                """;
            sessionCommand.Parameters.AddWithValue("$sessionId", session.SessionId);
            sessionCommand.Parameters.AddWithValue("$threadName", session.ThreadName);
            sessionCommand.Parameters.AddWithValue("$preferredPath", session.PreferredCopy.FilePath);
            sessionCommand.Parameters.AddWithValue("$readableTranscript", effectiveSearchDocument.ReadableTranscript);
            sessionCommand.Parameters.AddWithValue("$dialogueTranscript", effectiveSearchDocument.DialogueTranscript);
            sessionCommand.Parameters.AddWithValue("$toolSummary", effectiveSearchDocument.ToolSummary);
            sessionCommand.Parameters.AddWithValue("$commandText", effectiveSearchDocument.CommandText);
            sessionCommand.Parameters.AddWithValue("$filePaths", string.Join('\n', effectiveSearchDocument.FilePaths));
            sessionCommand.Parameters.AddWithValue("$urls", string.Join('\n', effectiveSearchDocument.Urls));
            sessionCommand.Parameters.AddWithValue("$errorText", effectiveSearchDocument.ErrorText);
            sessionCommand.Parameters.AddWithValue("$alias", effectiveSearchDocument.Alias);
            sessionCommand.Parameters.AddWithValue("$tags", string.Join('\n', effectiveSearchDocument.Tags));
            sessionCommand.Parameters.AddWithValue("$notes", effectiveSearchDocument.Notes);
            sessionCommand.Parameters.AddWithValue("$combinedText", effectiveSearchDocument.CombinedText);
            await sessionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCopies = connection.CreateCommand())
        {
            deleteCopies.CommandText = "DELETE FROM session_copies WHERE session_id = $sessionId;";
            deleteCopies.Parameters.AddWithValue("$sessionId", session.SessionId);
            await deleteCopies.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var copy in session.PhysicalCopies)
        {
            await using var copyCommand = connection.CreateCommand();
            copyCommand.CommandText =
                """
                INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
                VALUES ($sessionId, $filePath, $storeKind, $lastWriteUtc, $fileSizeBytes, $isHot);
                """;
            copyCommand.Parameters.AddWithValue("$sessionId", copy.SessionId);
            copyCommand.Parameters.AddWithValue("$filePath", copy.FilePath);
            copyCommand.Parameters.AddWithValue("$storeKind", (int)copy.StoreKind);
            copyCommand.Parameters.AddWithValue("$lastWriteUtc", copy.LastWriteTimeUtc.UtcDateTime.ToString("O"));
            copyCommand.Parameters.AddWithValue("$fileSizeBytes", copy.FileSizeBytes);
            copyCommand.Parameters.AddWithValue("$isHot", copy.IsHot ? 1 : 0);
            await copyCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SessionSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT session_id, thread_name, preferred_path, combined_text
            FROM sessions
            WHERE combined_text LIKE $pattern OR thread_name LIKE $pattern
            ORDER BY thread_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$pattern", $"%{query}%");

        var results = new List<SessionSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SessionSearchHit(
                SessionId: reader.GetString(0),
                ThreadName: reader.GetString(1),
                PreferredPath: reader.GetString(2),
                Snippet: reader.GetString(3),
                Score: 1));
        }

        return results;
    }

    public async Task SaveMetadataAsync(string sessionId, string alias, IReadOnlyList<string> tags, string notes, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE sessions
            SET alias = $alias,
                tags = $tags,
                notes = $notes,
                combined_text = trim(readable_transcript || char(10) || dialogue_transcript || char(10) || tool_summary || char(10) || command_text || char(10) || file_paths || char(10) || urls || char(10) || error_text || char(10) || $alias || char(10) || $tags || char(10) || $notes)
            WHERE session_id = $sessionId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$alias", alias);
        command.Parameters.AddWithValue("$tags", string.Join('\n', tags));
        command.Parameters.AddWithValue("$notes", notes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task UpdateMetadataAsync(string sessionId, string alias, IReadOnlyList<string> tags, string notes, CancellationToken cancellationToken) =>
        SaveMetadataAsync(sessionId, alias, tags, notes, cancellationToken);

    public async Task<IReadOnlyList<IndexedLogicalSession>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var copiesBySession = new Dictionary<string, List<SessionPhysicalCopy>>(StringComparer.Ordinal);
        await using (var copiesCommand = connection.CreateCommand())
        {
            copiesCommand.CommandText =
                """
                SELECT session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot
                FROM session_copies
                ORDER BY session_id, file_path;
                """;

            await using var reader = await copiesCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sessionId = reader.GetString(0);
                if (!copiesBySession.TryGetValue(sessionId, out var copies))
                {
                    copies = [];
                    copiesBySession[sessionId] = copies;
                }

                copies.Add(
                    new SessionPhysicalCopy(
                        sessionId,
                        reader.GetString(1),
                        (SessionStoreKind)reader.GetInt32(2),
                        DateTimeOffset.Parse(reader.GetString(3)),
                        reader.GetInt64(4),
                        reader.GetInt32(5) == 1));
            }
        }

        var sessions = new List<IndexedLogicalSession>();
        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText =
                """
                SELECT session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes
                FROM sessions
                ORDER BY thread_name COLLATE NOCASE;
                """;

            await using var reader = await sessionCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sessionId = reader.GetString(0);
                var threadName = reader.GetString(1);
                var preferredPath = reader.GetString(2);
                var copies = copiesBySession.TryGetValue(sessionId, out var existingCopies)
                    ? existingCopies
                    : [];

                var preferredCopy = copies.FirstOrDefault(copy => string.Equals(copy.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase))
                    ?? new SessionPhysicalCopy(sessionId, preferredPath, SessionStoreKind.Unknown, DateTimeOffset.MinValue, 0, false);

                sessions.Add(
                    new IndexedLogicalSession(
                        SessionId: sessionId,
                        ThreadName: threadName,
                        PreferredCopy: preferredCopy,
                        PhysicalCopies: copies.Count > 0 ? copies : [preferredCopy],
                        SearchDocument: new SessionSearchDocument(
                            ReadableTranscript: reader.GetString(3),
                            DialogueTranscript: reader.GetString(4),
                            ToolSummary: reader.GetString(5),
                            CommandText: reader.GetString(6),
                            FilePaths: SplitLines(reader.GetString(7)),
                            Urls: SplitLines(reader.GetString(8)),
                            ErrorText: reader.GetString(9),
                            Alias: reader.GetString(10),
                            Tags: SplitLines(reader.GetString(11)),
                            Notes: reader.GetString(12))));
            }
        }

        return sessions;
    }

    private static async Task<SessionSearchDocument> MergeExistingMetadataAsync(
        SqliteConnection connection,
        IndexedLogicalSession session,
        CancellationToken cancellationToken)
    {
        await using var existingCommand = connection.CreateCommand();
        existingCommand.CommandText = "SELECT alias, tags, notes FROM sessions WHERE session_id = $sessionId;";
        existingCommand.Parameters.AddWithValue("$sessionId", session.SessionId);

        await using var reader = await existingCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return session.SearchDocument;
        }

        var alias = string.IsNullOrWhiteSpace(session.SearchDocument.Alias)
            ? reader.GetString(0)
            : session.SearchDocument.Alias;
        var tags = session.SearchDocument.Tags.Count == 0
            ? SplitLines(reader.GetString(1))
            : session.SearchDocument.Tags;
        var notes = string.IsNullOrWhiteSpace(session.SearchDocument.Notes)
            ? reader.GetString(2)
            : session.SearchDocument.Notes;

        return session.SearchDocument with
        {
            Alias = alias,
            Tags = tags,
            Notes = notes
        };
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static IReadOnlyList<string> SplitLines(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
