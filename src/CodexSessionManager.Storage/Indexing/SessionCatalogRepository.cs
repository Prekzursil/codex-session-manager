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
    private const string SearchSql =
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
    private const string InsertCopySql =
        """
        INSERT INTO session_copies(session_id, file_path, store_kind, last_write_utc, file_size_bytes, is_hot)
        VALUES ($sessionId, $filePath, $storeKind, $lastWriteUtc, $fileSizeBytes, $isHot);
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
    private const string SelectMetadataSql =
        """
        SELECT alias, tags, notes
        FROM sessions
        WHERE session_id = $sessionId;
        """;
    private const string DeleteSearchIndexSql = "DELETE FROM session_search;";
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
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = RequireConnection(await OpenConnectionAsync(cancellationToken));
        await EnsureSchemaAsync(connection, cancellationToken);
        await RefreshSearchIndexAsync(connection, cancellationToken);
    }

    public async Task UpsertAsync(IndexedLogicalSession session, CancellationToken cancellationToken)
    {
        var nonNullSession = session ?? throw new ArgumentNullException(nameof(session));
        await using var connection = RequireConnection(await OpenConnectionAsync(cancellationToken));
        var searchDocument = await MergeExistingMetadataAsync(connection, nonNullSession, cancellationToken);
        var sessionId = nonNullSession.SessionId;
        var threadName = nonNullSession.ThreadName;
        var preferredCopy = nonNullSession.PreferredCopy ?? throw new InvalidOperationException("Session is missing a preferred copy.");
        var physicalCopies = nonNullSession.PhysicalCopies ?? [];

        await using (var command = new SqliteCommand(UpsertSessionSql, connection))
        {
            command.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            command.Parameters.AddWithValue("$threadName", threadName);
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

        await ReplaceCopyRowsAsync(connection, sessionId, physicalCopies, cancellationToken);
        await RefreshSearchRowAsync(connection, sessionId, cancellationToken);
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

        await using var connection = RequireConnection(await OpenConnectionAsync(cancellationToken));
        await using var command = new SqliteCommand(SearchSql, connection);
        var ftsQuery = ToFtsQuery(query);
        command.Parameters.AddWithValue("$query", ftsQuery);

        var results = new List<SessionSearchHit>();
        var searchReader = await command.ExecuteReaderAsync(cancellationToken);
        await using var reader = RequireReader(searchReader, "Search query did not return a reader.");
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

        await using var connection = RequireConnection(await OpenConnectionAsync(cancellationToken));
        await using var command = new SqliteCommand(UpdateMetadataSql, connection);
        command.Parameters.AddWithValue(SessionIdParameterName, sessionId);
        command.Parameters.AddWithValue("$alias", alias);
        command.Parameters.AddWithValue("$tags", string.Join('\n', tags));
        command.Parameters.AddWithValue("$notes", notes);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await RefreshSearchRowAsync(connection, sessionId, cancellationToken);
    }

    public Task UpdateMetadataAsync(string sessionId, string alias, IReadOnlyList<string> tags, string notes, CancellationToken cancellationToken)
    {
        return SaveMetadataAsync(sessionId, alias, tags, notes, cancellationToken);
    }

    public async Task<IReadOnlyList<IndexedLogicalSession>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = RequireConnection(await OpenConnectionAsync(cancellationToken));
        var copiesBySession = await LoadCopiesBySessionAsync(connection, cancellationToken);
        return await LoadSessionsAsync(connection, copiesBySession, cancellationToken);
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

        var sessionSearchDocument = session.SearchDocument;
        var currentSearchDocument = sessionSearchDocument ?? throw new InvalidOperationException("Session is missing search metadata.");
        await using var command = new SqliteCommand(SelectMetadataSql, connection);
        var sessionId = session.SessionId;
        command.Parameters.AddWithValue(SessionIdParameterName, sessionId);
        var metadataReader = await command.ExecuteReaderAsync(cancellationToken);
        await using var reader = RequireReader(metadataReader, "Metadata query did not return a reader.");
        if (!await reader.ReadAsync(cancellationToken))
        {
            return currentSearchDocument;
        }

        return currentSearchDocument with
        {
            Alias = string.IsNullOrWhiteSpace(currentSearchDocument.Alias) ? ReadRequiredString(reader, 0) : currentSearchDocument.Alias,
            Tags = currentSearchDocument.Tags.Count == 0 ? SplitLines(ReadRequiredString(reader, 1)) : currentSearchDocument.Tags,
            Notes = string.IsNullOrWhiteSpace(currentSearchDocument.Notes) ? ReadRequiredString(reader, 2) : currentSearchDocument.Notes
        };
    }

    private static async Task RefreshSearchIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        await using (var deleteCommand = new SqliteCommand(DeleteSearchIndexSql, connection))
        {
            var deleteTask = deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            await deleteTask;
        }

        await using var insertCommand = new SqliteCommand(RebuildSearchIndexSql, connection);
        var insertTask = insertCommand.ExecuteNonQueryAsync(cancellationToken);
        await insertTask;
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

        await using (var deleteCommand = new SqliteCommand(DeleteSearchRowSql, connection))
        {
            deleteCommand.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            var deleteTask = deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            await deleteTask;
        }

        await using (var insertCommand = new SqliteCommand(InsertSearchRowSql, connection))
        {
            insertCommand.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            var insertTask = insertCommand.ExecuteNonQueryAsync(cancellationToken);
            await insertTask;
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var databasePath = _databasePath;
        var databaseDirectory = Path.GetDirectoryName(databasePath)!;
        Directory.CreateDirectory(databaseDirectory);
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        var nonNullValue = value ?? throw new ArgumentNullException(nameof(value));

        return string.IsNullOrWhiteSpace(nonNullValue)
            ? []
            : nonNullValue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ToFtsQuery(string query)
    {
        var nonNullQuery = query ?? throw new ArgumentNullException(nameof(query));
        return string.Join(
            " AND ",
            nonNullQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ToFtsToken));
    }

    private static string ToFtsToken(string token)
    {
        var nonNullToken = token ?? throw new ArgumentNullException(nameof(token));
        var escaped = nonNullToken.Replace("\"", "\"\"");
        return escaped.All(static ch => char.IsLetterOrDigit(ch) || ch == '_')
            ? $"{escaped}*"
            : $"\"{escaped}\"*";
    }

    private static string ReadRequiredString(SqliteDataReader reader, int ordinal)
    {
        var nonNullReader = reader ?? throw new ArgumentNullException(nameof(reader));
        var value = nonNullReader.GetString(ordinal);
        return value;
    }

    private static async Task ReplaceCopyRowsAsync(
        SqliteConnection connection,
        string sessionId,
        IReadOnlyList<SessionPhysicalCopy> physicalCopies,
        CancellationToken cancellationToken)
    {
        await using (var deleteCopies = new SqliteCommand(DeleteSessionCopiesSql, connection))
        {
            deleteCopies.Parameters.AddWithValue(SessionIdParameterName, sessionId);
            var deleteTask = deleteCopies.ExecuteNonQueryAsync(cancellationToken);
            await deleteTask;
        }

        foreach (var copy in physicalCopies)
        {
            await using var copyCommand = new SqliteCommand(InsertCopySql, connection);
            var copySessionId = copy.SessionId;
            var copyFilePath = copy.FilePath;
            var copyStoreKind = copy.StoreKind;
            var copyLastWriteUtc = copy.LastWriteTimeUtc.UtcDateTime.ToString("O");
            var copyFileSizeBytes = copy.FileSizeBytes;
            var copyIsHot = copy.IsHot ? 1 : 0;

            copyCommand.Parameters.AddWithValue(SessionIdParameterName, copySessionId);
            copyCommand.Parameters.AddWithValue("$filePath", copyFilePath);
            copyCommand.Parameters.AddWithValue("$storeKind", (int)copyStoreKind);
            copyCommand.Parameters.AddWithValue("$lastWriteUtc", copyLastWriteUtc);
            copyCommand.Parameters.AddWithValue("$fileSizeBytes", copyFileSizeBytes);
            copyCommand.Parameters.AddWithValue("$isHot", copyIsHot);
            var insertTask = copyCommand.ExecuteNonQueryAsync(cancellationToken);
            await insertTask;
        }
    }

    private static async Task<Dictionary<string, List<SessionPhysicalCopy>>> LoadCopiesBySessionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var copiesBySession = new Dictionary<string, List<SessionPhysicalCopy>>(StringComparer.Ordinal);
        await using var copiesCommand = new SqliteCommand(ListCopiesSql, connection);
        await using var reader = RequireReader(await copiesCommand.ExecuteReaderAsync(cancellationToken), "Copy query did not return a reader.");
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
        await using var sessionCommand = new SqliteCommand(ListSessionsSql, connection);
        var sessionReader = await sessionCommand.ExecuteReaderAsync(cancellationToken);
        await using var reader = RequireReader(sessionReader, "Session query did not return a reader.");
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = ReadRequiredString(reader, 0);
            var preferredPath = ReadRequiredString(reader, 2);
            List<SessionPhysicalCopy> copies;
            if (!copiesBySession.TryGetValue(sessionId, out var existingCopies))
            {
                copies = [];
            }
            else
            {
                copies = existingCopies;
            }
            var preferredCopy = copies.FirstOrDefault(copy => string.Equals(copy.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase))
                ?? new SessionPhysicalCopy(
                    sessionId,
                    preferredPath,
                    SessionStoreKind.Unknown,
                    new SessionPhysicalCopyState(DateTimeOffset.MinValue, 0, false));

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

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(new SqliteCommand(CreateSessionsSql, connection), cancellationToken);
        await ExecuteNonQueryAsync(new SqliteCommand(CreateCopiesSql, connection), cancellationToken);
        await ExecuteNonQueryAsync(new SqliteCommand(CreateSearchSql, connection), cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var disposableCommand = command;
        var executeTask = disposableCommand.ExecuteNonQueryAsync(cancellationToken);
        await executeTask;
    }

    private static SqliteConnection RequireConnection(SqliteConnection? connection) =>
        connection ?? throw new InvalidOperationException("Failed to open the catalog database.");

    private static SqliteDataReader RequireReader(SqliteDataReader? reader, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(errorMessage));
        }

        return reader ?? throw new InvalidOperationException(errorMessage);
    }
}

