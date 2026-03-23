using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Tests;

public sealed class SessionDeduplicatorTests
{
    [Fact]
    public void Consolidate_GroupsBySessionId_PrefersLiveCopy_AndPreservesSiblings()
    {
        var liveCopy = new SessionPhysicalCopy(
            "session-1",
            @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl",
            SessionStoreKind.Live,
            new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero),
            1000,
            false);

        var backupCopy = new SessionPhysicalCopy(
            "session-1",
            @"C:\Users\Prekzursil\.codex\sessions_backup\2026\03\23\session-1.jsonl",
            SessionStoreKind.Backup,
            new DateTimeOffset(2026, 3, 23, 9, 0, 0, TimeSpan.Zero),
            1000,
            false);

        var logicalSessions = SessionDeduplicator.Consolidate([backupCopy, liveCopy]);

        var logical = Assert.Single(logicalSessions);
        Assert.Equal("session-1", logical.SessionId);
        Assert.Equal(liveCopy.FilePath, logical.PreferredCopy.FilePath);
        Assert.Equal(2, logical.PhysicalCopies.Count);
        Assert.Contains(logical.PhysicalCopies, copy => copy.FilePath == backupCopy.FilePath);
    }
}
