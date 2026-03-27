using CodexSessionManager.Core.Sessions;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace CodexSessionManager.Storage.Indexing;

public sealed class SessionCatalogRepository
{
    private const string SessionIdParameterName = "$sessionId";
    private const string DeleteSessionCopiesSql = "DELETE FROM session_copies WHERE session_id = $sessionId;";
    private const string CreateSessionsSql =
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
    private const string CreateCopiesSql =
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
    private const string CreateSearchSql =
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS session_search
        USING fts5(session_id UNINDEXED, combined_text);
        """;
    private const string UpsertSessionSql =
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
    private const string InsertCopySql =
        """
        INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
        VALUES ($sessionId, $filePath, $storeKind, $lastWriteUtc, $fileSizeBytes, $isHot);
        """;
    private const string SearchSessionsSql =
        """
        SELECT s.session_id, s.thread_name, s.preferred_path, coalesce(snippet(session_search, 1, '[', ']', '...', 10), '') AS snippet
        FROM session_search
        INNER JOIN sessions s ON s.session_id = session_search.session_id
        WHERE session_search MATCH $query
        ORDER BY rank;
        """;
    private const string UpdateMetadataSql =
        """
        UPDATE sessions
        SET alias = $alias,
            tags = $tags,
            notes = $notes,
            combined_text = trim(readable_transcript || char(10) || dialogue_transcript || char(10) || tool_summary || char(10) || command_text || char(10) || file_paths || char(10) || urls || char(10) || error_text || char(10) || $alias || char(10) || $tags || char(10) || $notes)
        WHERE session_id = $sessionId;
        """;
    private const string ListCopiesSql =
        """
        SELECT session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot
        FROM session_copies
        ORDER BY session_id, file_path;
        """;
    private const string ListSessionsSql =
        """
        SELECT session_id, thread_name, preferred_path, readable_transcript, dialogue_transcript, tool_summary, command_text, file_paths, urls, error_text, alias, tags, notes
        FROM sessions
        ORDER BY thread_name COLLATE NOCASE;
        """;
    private const string SelectSessionMetadataSql =
        """
        SELECT alias, tags, notes
        FROM sessions
        WHERE session_id = $sessionId;
        """;
    private const string ClearSearchIndexSql = "DELETE FROM session_search;";
    private const string RebuildSearchIndexSql =
        """
        INSERT INTO session_search(session_id, combined_text)
        SELECT session_id, combined_text
        FROM sessions;
        """;
    private const string DeleteSearchRowSql = "DELETE FROM session_search WHERE session_id = $sessionId;";
    private const string InsertSearchRowSql =
        """
        INSERT INTO session_search(session_id, combined_text)
        SELECT session_id, combined_text
        FROM sessions
        WHERE session_id = $sessionId;
        """;

    private readonly string _databasePath;

    public SessionCatalogRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await RefreshSearchIndexAsync(connection, cancellationToken);
    }

    public async Task UpsertAsync(IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var searchDocument = await MergeExistingMetadataAsync(connection, session, cancellationToken);
        var snapshot = CreateSnapshot(session, searchDocument);

        await UpsertSessionRowAsync(connection, snapshot, cancellationToken);
        await ReplaceCopyRowsAsync(connection, snapshot, cancellationToken);
        await RefreshSearchRowAsync(connection, snapshot.SessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, SearchSessionsSql);
        command.Parameters.AddWithValue("$query", ToFtsQuery(query));

        var results = new List<SessionSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SessionSearchHit(
                ReadRequiredString(reader, 0),
                ReadRequiredString(reader, 1),
                ReadRequiredString(reader, 2),
                ReadRequiredString(reader, 3),
                1));
        }

        return results;
    }

    public async Task SaveMetadataAsync(string sessionId, string alias, IReadOnlyList<string> tags, string notes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(tags);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, UpdateMetadataSql);
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
        var copiesBySession = await LoadCopiesBySessionAsync(connection, cancellationToken);
        return await LoadSessionsAsync(connection, copiesBySession, cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, CreateSessionsSql, cancellationToken);
        await ExecuteNonQueryAsync(connection, CreateCopiesSql, cancellationToken);
        await ExecuteNonQueryAsync(connection, CreateSearchSql, cancellationToken);
    }

    private static SessionSnapshot CreateSnapshot(IndexedLogicalSession session, SessionSearchDocument searchDocument)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(searchDocument);

        return new SessionSnapshot(
            session.SessionId,
            session.ThreadName,
            session.PreferredCopy ?? throw new InvalidOperationException("Session is missing a preferred copy."),
            session.PhysicalCopies ?? [],
            searchDocument);
    }

    private static async Task UpsertSessionRowAsync(SqliteConnection connection, SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, UpsertSessionSql);
        command.Parameters.AddWithValue(SessionIdParameterName, snapshot.SessionId);
        command.Parameters.AddWithValue("$threadName", snapshot.ThreadName);
        command.Parameters.AddWithValue("$preferredPath", snapshot.PreferredCopy.FilePath);
        command.Parameters.AddWithValue("$readableTranscript", snapshot.SearchDocument.ReadableTranscript);
        command.Parameters.AddWithValue("$dialogueTranscript", snapshot.SearchDocument.DialogueTranscript);
        command.Parameters.AddWithValue("$toolSummary", snapshot.SearchDocument.ToolSummary);
        command.Parameters.AddWithValue("$commandText", snapshot.SearchDocument.CommandText);
        command.Parameters.AddWithValue("$filePaths", string.Join('\n', snapshot.SearchDocument.FilePaths));
        command.Parameters.AddWithValue("$urls", string.Join('\n', snapshot.SearchDocument.Urls));
        command.Parameters.AddWithValue("$errorText", snapshot.SearchDocument.ErrorText);
        command.Parameters.AddWithValue("$alias", snapshot.SearchDocument.Alias);
        command.Parameters.AddWithValue("$tags", string.Join('\n', snapshot.SearchDocument.Tags));
        command.Parameters.AddWithValue("$notes", snapshot.SearchDocument.Notes);
        command.Parameters.AddWithValue("$combinedText", snapshot.SearchDocument.CombinedText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceCopyRowsAsync(SqliteConnection connection, SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using (var deleteCommand = CreateCommand(connection, DeleteSessionCopiesSql))
        {
            deleteCommand.Parameters.AddWithValue(SessionIdParameterName, snapshot.SessionId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var copy in snapshot.PhysicalCopies)
        {
            await using var copyCommand = CreateCommand(connection, InsertCopySql);
            copyCommand.Parameters.AddWithValue(SessionIdParameterName, copy.SessionId);
            copyCommand.Parameters.AddWithValue("$filePath", copy.FilePath);
            copyCommand.Parameters.AddWithValue("$storeKind", (int)copy.StoreKind);
            copyCommand.Parameters.AddWithValue("$lastWriteUtc", copy.LastWriteTimeUtc.UtcDateTime.ToString("O"));
            copyCommand.Parameters.AddWithValue("$fileSizeBytes", copy.FileSizeBytes);
            copyCommand.Parameters.AddWithValue("$isHot", copy.IsHot ? 1 : 0);
            await copyCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Dictionary<string, List<SessionPhysicalCopy>>> LoadCopiesBySessionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var copiesBySession = new Dictionary<string, List<SessionPhysicalCopy>>(StringComparer.Ordinal);
        await using var command = CreateCommand(connection, ListCopiesSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

        return copiesBySession;
    }

    private static async Task<IReadOnlyList<IndexedLogicalSession>> LoadSessionsAsync(
        SqliteConnection connection,
        IReadOnlyDictionary<string, List<SessionPhysicalCopy>> copiesBySession,
        CancellationToken cancellationToken)
    {
        var sessions = new List<IndexedLogicalSession>();
        await using var command = CreateCommand(connection, ListSessionsSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = ReadRequiredString(reader, 0);
            var preferredPath = ReadRequiredString(reader, 2);
            var copies = copiesBySession.TryGetValue(sessionId, out var existingCopies) ? existingCopies : [];
            var preferredCopy = ResolvePreferredCopy(sessionId, preferredPath, copies);

            sessions.Add(new IndexedLogicalSession(
                sessionId,
                ReadRequiredString(reader, 1),
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

        return sessions;
    }

    private static SessionPhysicalCopy ResolvePreferredCopy(string sessionId, string preferredPath, IReadOnlyList<SessionPhysicalCopy> copies)
    {
        var preferredCopy = copies.FirstOrDefault(copy => string.Equals(copy.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase));
        return preferredCopy ?? new SessionPhysicalCopy(
            sessionId,
            preferredPath,
            SessionStoreKind.Unknown,
            new SessionPhysicalCopyState(DateTimeOffset.MinValue, 0, false));
    }

    private static async Task<SessionSearchDocument> MergeExistingMetadataAsync(SqliteConnection connection, IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);

        await using var command = CreateCommand(connection, SelectSessionMetadataSql);
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
        await ExecuteNonQueryAsync(connection, ClearSearchIndexSql, cancellationToken);
        await ExecuteNonQueryAsync(connection, RebuildSearchIndexSql, cancellationToken);
    }

    private static async Task RefreshSearchRowAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(sessionId));
        }

        await using (var deleteCommand = CreateCommand(connection, DeleteSearchRowSql))
        {
            deleteCommand.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertCommand = CreateCommand(connection, InsertSearchRowSql))
        {
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

    private static SqliteCommand CreateCommand(SqliteConnection connection, string commandText)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(commandText);
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, commandText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ToFtsQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return string.Join(
            " AND ",
            query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ToFtsToken));
    }

    private static string ToFtsToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var escaped = token.Replace("\"", "\"\"");
        return escaped.All(static ch => char.IsLetterOrDigit(ch) || ch == '_')
            ? $"{escaped}*"
            : $"\"{escaped}\"*";
    }

    private static string ReadRequiredString(SqliteDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.GetString(ordinal);
    }

    private readonly record struct SessionSnapshot(
        string SessionId,
        string ThreadName,
        SessionPhysicalCopy PreferredCopy,
        IReadOnlyList<SessionPhysicalCopy> PhysicalCopies,
        SessionSearchDocument SearchDocument);
}
