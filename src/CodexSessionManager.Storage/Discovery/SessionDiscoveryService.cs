using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Discovery;

public static class SessionDiscoveryService
{
    public static async Task<DiscoveredSessionCatalog> DiscoverAsync(IEnumerable<SessionStoreRoot> roots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var stores = roots.Select(root =>
        {
            var normalizedRoot = root.RootPath.Replace('/', '\\').TrimEnd('\\');
            var backupWorkspaceRoot = Path.GetDirectoryName(normalizedRoot);
            var normalizedBackupWorkspaceRoot = string.IsNullOrWhiteSpace(backupWorkspaceRoot) ? normalizedRoot : backupWorkspaceRoot;
            return root.StoreKind switch
            {
                SessionStoreKind.Live => new KnownSessionStore(normalizedRoot, root.StoreKind, Path.Combine(normalizedRoot, "sessions"), Path.Combine(normalizedRoot, "session_index.jsonl")),
                SessionStoreKind.Backup when normalizedRoot.EndsWith(@"\sessions_backup", StringComparison.OrdinalIgnoreCase)
                    => new KnownSessionStore(normalizedBackupWorkspaceRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedBackupWorkspaceRoot, "session_index.jsonl")),
                _ => new KnownSessionStore(normalizedRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedRoot, "session_index.jsonl"))
            };
        });

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores, cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }
}
