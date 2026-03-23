using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public static class KnownStoreLocator
{
    public static IReadOnlyList<KnownSessionStore> GetKnownStores(string codexHome)
    {
        return
        [
            new KnownSessionStore(
                codexHome,
                SessionStoreKind.Live,
                Path.Combine(codexHome, "sessions"),
                Path.Combine(codexHome, "session_index.jsonl")),
            new KnownSessionStore(
                codexHome,
                SessionStoreKind.Backup,
                Path.Combine(codexHome, "sessions_backup"),
                Path.Combine(codexHome, "session_index.jsonl"))
        ];
    }
}
