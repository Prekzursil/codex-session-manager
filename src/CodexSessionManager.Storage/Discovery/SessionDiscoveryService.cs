using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Discovery;

public static class SessionDiscoveryService
{
    public static async Task<DiscoveredSessionCatalog> DiscoverAsync(IEnumerable<SessionStoreRoot> roots, CancellationToken cancellationToken)
    {
        if (roots is null)
        {
            throw new ArgumentNullException(nameof(roots));
        }

        var stores = roots.Select(CreateKnownSessionStore).ToArray();

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores, cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }

    private static KnownSessionStore CreateKnownSessionStore(SessionStoreRoot root)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var normalizedRoot = NormalizeRootPath(root.RootPath);
        var backupWorkspaceRoot = Path.GetDirectoryName(normalizedRoot);
        var normalizedBackupWorkspaceRoot = string.IsNullOrWhiteSpace(backupWorkspaceRoot) ? normalizedRoot : backupWorkspaceRoot;

        return root.StoreKind switch
        {
            SessionStoreKind.Live => new KnownSessionStore(normalizedRoot, root.StoreKind, Path.Combine(normalizedRoot, "sessions"), Path.Combine(normalizedRoot, "session_index.jsonl")),
            SessionStoreKind.Backup when normalizedRoot.EndsWith($"{Path.DirectorySeparatorChar}sessions_backup", StringComparison.OrdinalIgnoreCase)
                => new KnownSessionStore(normalizedBackupWorkspaceRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedBackupWorkspaceRoot, "session_index.jsonl")),
            _ => new KnownSessionStore(normalizedRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedRoot, "session_index.jsonl"))
        };
    }

    private static string NormalizeRootPath(string rootPath)
    {
        if (rootPath is null)
        {
            throw new ArgumentNullException(nameof(rootPath));
        }

        return rootPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
    }
}

