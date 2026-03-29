#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_LoadsKnownStores_AppliesSessionIndexMetadata_AndDedupesCopiesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var liveDir = Path.Combine(root, ".codex", "sessions", "2026", "03", "23");
        var backupDir = Path.Combine(root, ".codex", "sessions_backup", "2026", "03", "23");
        Directory.CreateDirectory(liveDir);
        Directory.CreateDirectory(backupDir);

        var sessionIndexPath = Path.Combine(root, ".codex", "session_index.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(sessionIndexPath)!);
        await File.WriteAllTextAsync(sessionIndexPath, """{"id":"session-1","thread_name":"Renderer work","updated_at":"2026-03-23T10:00:00Z"}""" + Environment.NewLine);

        var sessionContents = string.Join(
            Environment.NewLine,
            [
                """{"timestamp":"2026-03-23T00:17:23.757Z","type":"session_meta","payload":{"id":"session-1","timestamp":"2026-03-23T00:17:23.757Z","cwd":"C:\\Users\\Prekzursil","originator":"codex_cli_rs","source":"cli","model_provider":"openai"}}""",
                """{"timestamp":"2026-03-23T00:17:25.000Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"inspect renderer logic"}]}}""",
                """{"timestamp":"2026-03-23T00:17:28.000Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"I found the renderer."}]}}"""
            ]);

        var liveFile = Path.Combine(liveDir, "session-1.jsonl");
        var backupFile = Path.Combine(backupDir, "session-1.jsonl");
        await File.WriteAllTextAsync(liveFile, sessionContents);
        await File.WriteAllTextAsync(backupFile, sessionContents);

        try
        {
            var catalog = await SessionDiscoveryService.DiscoverAsync(new[]
            {
                new SessionStoreRoot(Path.Combine(root, ".codex"), SessionStoreKind.Live),
                new SessionStoreRoot(Path.Combine(root, ".codex", "sessions_backup"), SessionStoreKind.Backup)
            }, CancellationToken.None);

            var logical = Assert.Single(catalog.LogicalSessions);
            Assert.Equal("session-1", logical.SessionId);
            Assert.Equal("Renderer work", logical.ThreadName);
            Assert.Equal(2, logical.PhysicalCopies.Count);
            Assert.Equal(SessionStoreKind.Live, logical.PreferredCopy.StoreKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

