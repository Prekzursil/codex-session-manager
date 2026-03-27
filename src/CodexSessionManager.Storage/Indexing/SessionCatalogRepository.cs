// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace CodexSessionManager.Storage.Indexing;

public sealed class SessionCatalogRepository
{
    private const string SessionIdParameterName = "$sessionId";
    private const string DeleteSessionCopiesSql = "DELETE FROM session_copies WHERE session_id = $sessionId;";
    private const string SelectSessionMetadataSql = "SELECT alias, tags, notes FROM sessions WHERE session_id = $sessionId;";
    private readonly string _databasePath;

    public SessionCatalogRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using (var createSessionsCommand = connection.CreateCommand())
        {
            createSessionsCommand.CommandText =
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
                """;
            await createSessionsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createCopiesCommand = connection.CreateCommand())
        {
            createCopiesCommand.CommandText =
                """
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
            await createCopiesCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createSearchCommand = connection.CreateCommand())
        {
            createSearchCommand.CommandText =
                """
                CREATE VIRTUAL TABLE IF NOT EXISTS session_search
                USING fts5(session_id UNINDEXED, combined_text);
                """;
            await createSearchCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await RefreshSearchIndexAsync(connection, cancellationToken);
    }

    public async Task UpsertAsync(IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var searchDocument = await MergeExistingMetadataAsync(connection, session, cancellationToken);
        var preferredCopy = session.PreferredCopy ?? throw new InvalidOperationException("Session is missing a preferred copy.");
        var physicalCopies = session.PhysicalCopies ?? [];

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
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
            command.Parameters.AddWithValue(SessionIdParameterName, session.SessionId);
            command.Parameters.AddWithValue("$threadName", session.ThreadName);
            command.Parameters.AddWithValue("$preferredPath", preferredCopy.FilePath);
            command.Parameters.AddWithValue("$readableTranscript", searchDocument.ReadableTranscript);
            command.Parameters.AddWithValue("$dialogueTranscript", searchDocument.DialogueTranscript);
            command.Parameters.AddWithValue("$toolSummary", searchDocument.ToolSummary);
            command.Parameters.AddWithValue("$commandText", searchDocument.CommandText);
            command.Parameters.AddWithValue("$filePaths", string.Join('\n', searchDocument.FilePaths));
            command.Parameters.AddWithValue("$urls", string.Join('\n', searchDocument.Urls));
            command.Parameters.AddWithValue("$errorText", searchDocument.ErrorText);
            command.Parameters.AddWithValue("$alias", searchDocument.Alias);
            command.Parameters.AddWithValue("$tags", string.Join('\n', searchDocument.Tags));
            command.Parameters.AddWithValue("$notes", searchDocument.Notes);
            command.Parameters.AddWithValue("$combinedText", searchDocument.CombinedText);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCopies = connection.CreateCommand())
        {
            // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- constant SQL text, parameter bound separately via SqliteParameter.
            deleteCopies.CommandText = DeleteSessionCopiesSql;
            deleteCopies.Parameters.AddWithValue(SessionIdParameterName, session.SessionId);
            await deleteCopies.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var copy in physicalCopies)
        {
            await using var copyCommand = connection.CreateCommand();
            copyCommand.CommandText =
                """
                INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
                VALUES ($sessionId, $filePath, $storeKind, $lastWriteUtc, $fileSizeBytes, $isHot);
                """;
            copyCommand.Parameters.AddWithValue(SessionIdParameterName, copy.SessionId);
            copyCommand.Parameters.AddWithValue("$filePath", copy.FilePath);
            copyCommand.Parameters.AddWithValue("$storeKind", (int)copy.StoreKind);
            copyCommand.Parameters.AddWithValue("$lastWriteUtc", copy.LastWriteTimeUtc.UtcDateTime.ToString("O"));
            copyCommand.Parameters.AddWithValue("$fileSizeBytes", copy.FileSizeBytes);
            copyCommand.Parameters.AddWithValue("$isHot", copy.IsHot ? 1 : 0);
            await copyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await RefreshSearchRowAsync(connection, session.SessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.session_id, s.thread_name, s.preferred_path, coalesce(snippet(session_search, 1, '[', ']', '...', 10), '') AS snippet
            FROM session_search
            INNER JOIN sessions s ON s.session_id = session_search.session_id
            WHERE session_search MATCH $query
            ORDER BY rank;
            """;
        command.Parameters.AddWithValue("$query", ToFtsQuery(query));

        var results = new List<SessionSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var snippet = ReadRequiredString(reader, 3);
            results.Add(new SessionSearchHit(ReadRequiredString(reader, 0), ReadRequiredString(reader, 1), ReadRequiredString(reader, 2), snippet, 1));
        }

        return results;
    }

    public async Task SaveMetadataAsync(string sessionId, string alias, IReadOnlyList<string> tags, string notes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(sessionId));
        }

        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags));
        }

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
        command.Parameters.AddWithValue(SessionIdParameterName, sessionId);
        command.Parameters.AddWithValue("$alias", alias);
        command.Parameters.AddWithValue("$tags", string.Join('\n', tags));
        command.Parameters.AddWithValue("$notes", notes);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await RefreshSearchRowAsync(connection, sessionId, cancellationToken);
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
                var sessionId = ReadRequiredString(reader, 0);
                if (!copiesBySession.TryGetValue(sessionId, out var copies))
                {
                    copies = [];
                    copiesBySession[sessionId] = copies;
                }

                copies.Add(new SessionPhysicalCopy(
                    sessionId,
                    ReadRequiredString(reader, 1),
                    (SessionStoreKind)reader.GetInt32(2),
                    new SessionPhysicalCopyState(
                        DateTimeOffset.Parse(ReadRequiredString(reader, 3), CultureInfo.InvariantCulture),
                        reader.GetInt64(4),
                        reader.GetInt32(5) == 1)));
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
                var sessionId = ReadRequiredString(reader, 0);
                var threadName = ReadRequiredString(reader, 1);
                var preferredPath = ReadRequiredString(reader, 2);
                var copies = copiesBySession.TryGetValue(sessionId, out var existingCopies) ? existingCopies : [];
                var preferredCopy = copies.FirstOrDefault(copy => string.Equals(copy.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase))
                    ?? new SessionPhysicalCopy(
                        sessionId,
                        preferredPath,
                        SessionStoreKind.Unknown,
                        new SessionPhysicalCopyState(DateTimeOffset.MinValue, 0, false));

                sessions.Add(
                    new IndexedLogicalSession(
                        sessionId,
                        threadName,
                        preferredCopy,
                        copies.Count > 0 ? copies : [preferredCopy],
                        new SessionSearchDocument
                        {
                            ReadableTranscript = ReadRequiredString(reader, 3),
                            DialogueTranscript = ReadRequiredString(reader, 4),
                            ToolSummary = ReadRequiredString(reader, 5),
                            CommandText = ReadRequiredString(reader, 6),
                            FilePaths = SplitLines(ReadRequiredString(reader, 7)),
                            Urls = SplitLines(ReadRequiredString(reader, 8)),
                            ErrorText = ReadRequiredString(reader, 9),
                            Alias = ReadRequiredString(reader, 10),
                            Tags = SplitLines(ReadRequiredString(reader, 11)),
                            Notes = ReadRequiredString(reader, 12)
                        }));
            }
        }

        return sessions;
    }

    private static async Task<SessionSearchDocument> MergeExistingMetadataAsync(SqliteConnection connection, IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        await using var command = connection.CreateCommand();
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- constant SQL text, parameter bound separately via SqliteParameter.
        command.CommandText = SelectSessionMetadataSql;
        command.Parameters.AddWithValue(SessionIdParameterName, session.SessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return session.SearchDocument;
        }

        var searchDocument = session.SearchDocument ?? throw new InvalidOperationException("Session is missing search metadata.");
        return searchDocument with
        {
            Alias = string.IsNullOrWhiteSpace(searchDocument.Alias) ? ReadRequiredString(reader, 0) : searchDocument.Alias,
            Tags = searchDocument.Tags.Count == 0 ? SplitLines(ReadRequiredString(reader, 1)) : searchDocument.Tags,
            Notes = string.IsNullOrWhiteSpace(searchDocument.Notes) ? ReadRequiredString(reader, 2) : searchDocument.Notes
        };
    }

    private static async Task RefreshSearchIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM session_search;";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO session_search(session_id, combined_text)
            SELECT session_id, combined_text
            FROM sessions;
            """;
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RefreshSearchRowAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(sessionId));
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM session_search WHERE session_id = $sessionId;";
            deleteCommand.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText =
                """
                INSERT INTO session_search(session_id, combined_text)
                SELECT session_id, combined_text
                FROM sessions
                WHERE session_id = $sessionId;
                """;
            insertCommand.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(_databasePath);
        Directory.CreateDirectory(string.IsNullOrWhiteSpace(directoryPath) ? "." : directoryPath);
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ToFtsQuery(string query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        return string.Join(
            " AND ",
            query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ToFtsToken));
    }

    private static string ToFtsToken(string token)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        var escaped = token.Replace("\"", "\"\"");
        return escaped.All(static ch => char.IsLetterOrDigit(ch) || ch == '_')
            ? $"{escaped}*"
            : $"\"{escaped}\"*";
    }

    private static string ReadRequiredString(SqliteDataReader reader, int ordinal)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        return reader.GetString(ordinal);
    }
}

