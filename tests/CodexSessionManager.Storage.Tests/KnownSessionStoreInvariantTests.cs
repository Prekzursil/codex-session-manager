// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using CodexSessionManager.Storage.Discovery;

namespace CodexSessionManager.Storage.Tests;

public sealed class KnownSessionStoreInvariantTests
{
    [Fact]
    public void KnownSessionStore_throws_when_required_paths_are_null()
    {
        Assert.Throws<ArgumentNullException>(() => new KnownSessionStore(null!, SessionStoreKind.Live, @"C:\.codex\sessions", @"C:\.codex\session_index.jsonl"));
        Assert.Throws<ArgumentNullException>(() => new KnownSessionStore(@"C:\.codex", SessionStoreKind.Live, null!, @"C:\.codex\session_index.jsonl"));
        Assert.Throws<ArgumentNullException>(() => new KnownSessionStore(@"C:\.codex", SessionStoreKind.Live, @"C:\.codex\sessions", null!));
    }
}

