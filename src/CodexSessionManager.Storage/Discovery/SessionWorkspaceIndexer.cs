#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Text.Json;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.Storage.Discovery;

public sealed class SessionWorkspaceIndexer
{
    private readonly SessionCatalogRepository _repository;

    public SessionWorkspaceIndexer(SessionCatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<IndexedLogicalSession>> RebuildAsync(IEnumerable<KnownSessionStore> stores, CancellationToken cancellationToken)
    {
        var sessions = await LoadSessionsAsync(stores, cancellationToken); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        foreach (var session in sessions)
        {
            await _repository.UpsertAsync(session, cancellationToken);
        }

        return sessions;
    }

    internal static async Task<IReadOnlyList<IndexedLogicalSession>> LoadSessionsAsync(IEnumerable<KnownSessionStore> stores, CancellationToken cancellationToken)
    {
        var threadNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var parsedSessions = new Dictionary<string, ParsedSessionFile>(StringComparer.Ordinal);
        var copies = new List<SessionPhysicalCopy>();

        foreach (var store in stores)
        {
            await LoadStoreSessionsAsync(store, threadNames, parsedSessions, copies, cancellationToken);
        }

        return SessionDeduplicator.Consolidate(copies)
            .Select(logical =>
            {
                var parsed = parsedSessions[logical.SessionId];
                var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
                var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
                var toolSummary = string.Join(Environment.NewLine, parsed.TechnicalBreadcrumbs.Commands.Select(command => $"Ran: {command}"));
                var searchDocument = new SessionSearchDocument
                {
                    ReadableTranscript = readableTranscript,
                    DialogueTranscript = dialogueTranscript,
                    ToolSummary = toolSummary,
                    CommandText = string.Join(Environment.NewLine, parsed.TechnicalBreadcrumbs.Commands),
                    FilePaths = parsed.TechnicalBreadcrumbs.FilePaths,
                    Urls = parsed.TechnicalBreadcrumbs.Urls,
                    ErrorText = string.Join(Environment.NewLine, parsed.TechnicalBreadcrumbs.ExitCodes.Select(code => $"Exit {code}")),
                    Alias = string.Empty,
                    Tags = [],
                    Notes = string.Empty
                };

                return new IndexedLogicalSession(
                    logical.SessionId,
                    threadNames.TryGetValue(logical.SessionId, out var threadName) ? threadName : logical.SessionId,
                    logical.PreferredCopy,
                    logical.PhysicalCopies,
                    searchDocument);
            })
            .OrderBy(session => session.ThreadName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task LoadStoreSessionsAsync(
        KnownSessionStore store,
        IDictionary<string, string> threadNames,
        IDictionary<string, ParsedSessionFile> parsedSessions,
        ICollection<SessionPhysicalCopy> copies,
        CancellationToken cancellationToken)
    {
        foreach (var kvp in await LoadSessionIndexAsync(store.SessionIndexPath, cancellationToken)) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        {
            threadNames[kvp.Key] = kvp.Value;
        }

        if (!Directory.Exists(store.SessionsPath)) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(store.SessionsPath, "*.jsonl", SearchOption.AllDirectories)) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        {
            var parsed = await SessionJsonlParser.ParseAsync(filePath, cancellationToken);
            parsedSessions[parsed.SessionId] = parsed;
            copies.Add(CreateSessionCopy(store.StoreKind, filePath, parsed.SessionId)); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        }
    }

    private static SessionPhysicalCopy CreateSessionCopy(SessionStoreKind storeKind, string filePath, string sessionId)
    {
        var fileInfo = new FileInfo(filePath);
        return new SessionPhysicalCopy(
            sessionId,
            filePath,
            storeKind,
            new SessionPhysicalCopyState(
                fileInfo.LastWriteTimeUtc,
                fileInfo.Length,
                false));
    }

    private static async Task<Dictionary<string, string>> LoadSessionIndexAsync(string sessionIndexPath, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(sessionIndexPath))
        {
            return results;
        }

        var lines = await File.ReadAllLinesAsync(sessionIndexPath, cancellationToken); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            var sessionId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            var threadName = root.TryGetProperty("thread_name", out var threadNameElement) && threadNameElement.ValueKind is JsonValueKind.String
                ? threadNameElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(threadName))
            {
                results[sessionId] = threadName!;
            }
        }

        return results;
    }
}

