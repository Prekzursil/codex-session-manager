using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Discovery;

public static class SessionDiscoveryService
{
    public static async Task<DiscoveredSessionCatalog> DiscoverAsync(IEnumerable<SessionStoreRoot> roots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var stores = roots.Select(CreateKnownSessionStore).ToArray();

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores, cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }

    private static KnownSessionStore CreateKnownSessionStore(SessionStoreRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var rootPath = root.RootPath;
        var storeKind = root.StoreKind;
        var normalizedRoot = NormalizeRootPath(rootPath);
        var backupWorkspaceRoot = Path.GetDirectoryName(normalizedRoot);
        var normalizedBackupWorkspaceRoot = string.IsNullOrWhiteSpace(backupWorkspaceRoot) ? normalizedRoot : backupWorkspaceRoot;

        if (storeKind == SessionStoreKind.Live)
        {
            return new KnownSessionStore(
                normalizedRoot,
                storeKind,
                Path.Combine(normalizedRoot, "sessions"),
                Path.Combine(normalizedRoot, "session_index.jsonl"));
        }

        if (storeKind == SessionStoreKind.Backup
            && normalizedRoot.EndsWith(
                $"{Path.DirectorySeparatorChar}sessions_backup",
                StringComparison.OrdinalIgnoreCase))
        {
            return new KnownSessionStore(
                normalizedBackupWorkspaceRoot,
                storeKind,
                normalizedRoot,
                Path.Combine(normalizedBackupWorkspaceRoot, "session_index.jsonl"));
        }

        return new KnownSessionStore(
            normalizedRoot,
            storeKind,
            normalizedRoot,
            Path.Combine(normalizedRoot, "session_index.jsonl"));
    }

    private static string NormalizeRootPath(string rootPath)
    {
        ArgumentNullException.ThrowIfNull(rootPath);

        var normalizedRootPath = rootPath.Replace('\\', Path.DirectorySeparatorChar);
        normalizedRootPath = normalizedRootPath.Replace('/', Path.DirectorySeparatorChar);
        return normalizedRootPath.TrimEnd(Path.DirectorySeparatorChar);
    }
}

