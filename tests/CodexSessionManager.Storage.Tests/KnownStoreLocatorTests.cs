using CodexSessionManager.Core.Sessions;
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
