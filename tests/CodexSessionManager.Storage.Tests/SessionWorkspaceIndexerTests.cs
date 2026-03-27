using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionWorkspaceIndexerTests
{
    [Fact]
    public async Task Rebuild_IndexesKnownStores_AndDeduplicatesLiveFirstAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"csm-{Guid.NewGuid():N}");
        var liveRoot = Path.Combine(root, ".codex");
        var liveSessions = Path.Combine(liveRoot, "sessions", "2026", "03", "23");
        var backupSessions = Path.Combine(liveRoot, "sessions_backup", "2026", "03", "23");
        Directory.CreateDirectory(liveSessions);
        Directory.CreateDirectory(backupSessions);

        var sessionFileName = "rollout-2026-03-23T00-17-23-session-1.jsonl";
        var sessionJsonl =
            """
            {"timestamp":"2026-03-23T00:17:23.757Z","type":"session_meta","payload":{"id":"session-1","timestamp":"2026-03-23T00:17:23.757Z","cwd":"C:\Users\Prekzursil","originator":"codex_cli_rs","source":"cli","model_provider":"openai"}}
            {"timestamp":"2026-03-23T00:17:25.000Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"search this phrase"}]}}
            {"timestamp":"2026-03-23T00:17:28.000Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"I found the renderer."}]}}
            """;

        await File.WriteAllTextAsync(Path.Combine(liveSessions, sessionFileName), sessionJsonl);
        await File.WriteAllTextAsync(Path.Combine(backupSessions, sessionFileName), sessionJsonl);
        await File.WriteAllTextAsync(
            Path.Combine(liveRoot, "session_index.jsonl"),
            """{"id":"session-1","thread_name":"Renderer work","updated_at":"2026-03-23T00:19:00Z"}""" + Environment.NewLine);

        var databasePath = Path.Combine(root, "catalog.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);
            var indexer = new SessionWorkspaceIndexer(repository);

            var result = await indexer.RebuildAsync(new[]
            {
                new KnownSessionStore(liveRoot, SessionStoreKind.Live, Path.Combine(liveRoot, "sessions"), Path.Combine(liveRoot, "session_index.jsonl")),
                new KnownSessionStore(liveRoot, SessionStoreKind.Backup, Path.Combine(liveRoot, "sessions_backup"), Path.Combine(liveRoot, "session_index.jsonl"))
            }, CancellationToken.None);

            var logical = Assert.Single(result);
            Assert.Equal("Renderer work", logical.ThreadName);
            Assert.Equal(SessionStoreKind.Live, logical.PreferredCopy.StoreKind);

            var persisted = await repository.ListSessionsAsync(CancellationToken.None);
            var persistedLogical = Assert.Single(persisted);
            Assert.Equal("session-1", persistedLogical.SessionId);
            Assert.Equal(2, persistedLogical.PhysicalCopies.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

