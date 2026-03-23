using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Discovery;

public sealed class SessionDiscoveryService
{
    public async Task<DiscoveredSessionCatalog> DiscoverAsync(IEnumerable<SessionStoreRoot> roots, CancellationToken cancellationToken)
    {
        var stores = roots.Select(root =>
        {
            var normalizedRoot = root.RootPath.Replace('/', '\\').TrimEnd('\\');
            return root.StoreKind switch
            {
                SessionStoreKind.Live => new KnownSessionStore(normalizedRoot, root.StoreKind, Path.Combine(normalizedRoot, "sessions"), Path.Combine(normalizedRoot, "session_index.jsonl")),
                SessionStoreKind.Backup when normalizedRoot.EndsWith(@"\sessions_backup", StringComparison.OrdinalIgnoreCase)
                    => new KnownSessionStore(Path.GetDirectoryName(normalizedRoot) ?? normalizedRoot, root.StoreKind, normalizedRoot, Path.Combine(Path.GetDirectoryName(normalizedRoot) ?? normalizedRoot, "session_index.jsonl")),
                _ => new KnownSessionStore(normalizedRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedRoot, "session_index.jsonl"))
            };
        });

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores, cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }
}
