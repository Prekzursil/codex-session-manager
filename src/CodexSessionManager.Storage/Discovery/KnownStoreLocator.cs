using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public static class KnownStoreLocator
{
    public static IReadOnlyList<KnownSessionStore> GetKnownStores(string codexHome)
    {
        ArgumentNullException.ThrowIfNull(codexHome);

        var workspaceRoot = codexHome;
        return
        [
            new KnownSessionStore(
                workspaceRoot,
                SessionStoreKind.Live,
                Path.Combine(workspaceRoot, "sessions"),
                Path.Combine(workspaceRoot, "session_index.jsonl")),
            new KnownSessionStore(
                workspaceRoot,
                SessionStoreKind.Backup,
                Path.Combine(workspaceRoot, "sessions_backup"),
                Path.Combine(workspaceRoot, "session_index.jsonl"))
        ];
    }
}

