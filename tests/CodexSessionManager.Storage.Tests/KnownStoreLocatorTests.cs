// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using CodexSessionManager.Storage.Discovery;

namespace CodexSessionManager.Storage.Tests;

public sealed class KnownStoreLocatorTests
{
    [Fact]
    public void GetKnownStores_ReturnsCanonicalLiveAndBackupStores()
    {
        var home = @"C:\Users\Prekzursil\.codex";

        var stores = KnownStoreLocator.GetKnownStores(home);

        Assert.Collection(
            stores,
            live =>
            {
                Assert.Equal(SessionStoreKind.Live, live.StoreKind);
                Assert.Equal(Path.Combine(home, "sessions"), live.SessionsPath);
            },
            backup =>
            {
                Assert.Equal(SessionStoreKind.Backup, backup.StoreKind);
                Assert.Equal(Path.Combine(home, "sessions_backup"), backup.SessionsPath);
            });
    }
}

