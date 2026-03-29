#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Discovery;

public static class SessionDiscoveryService
{
    public static async Task<DiscoveredSessionCatalog> DiscoverAsync(IEnumerable<SessionStoreRoot> roots, CancellationToken cancellationToken)
    {
        var sessionRoots = roots ?? throw new ArgumentNullException(nameof(roots));

        var stores = new List<KnownSessionStore>();
        foreach (var root in sessionRoots)
        {
            var sessionRoot = root ?? throw new ArgumentNullException(nameof(roots));
            stores.Add(CreateKnownSessionStore(sessionRoot));
        }

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores.ToArray(), cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }

    private static KnownSessionStore CreateKnownSessionStore(SessionStoreRoot root)
    {
        var sessionRoot = root ?? throw new ArgumentNullException(nameof(root));

        var rootPath = sessionRoot.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Session store root path cannot be empty.", nameof(root));
        }

        var storeKind = sessionRoot.StoreKind;
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
        var rawRootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        var normalizedRootPath = rawRootPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var filesystemRoot = Path.GetPathRoot(normalizedRootPath);
        var trimmedRootPath = normalizedRootPath.TrimEnd(Path.DirectorySeparatorChar);

        if (string.IsNullOrEmpty(trimmedRootPath) && !string.IsNullOrEmpty(filesystemRoot))
        {
            return filesystemRoot;
        }

        if (!string.IsNullOrEmpty(filesystemRoot)
            && string.Equals(trimmedRootPath, filesystemRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return filesystemRoot;
        }

        return trimmedRootPath;
    }
}
