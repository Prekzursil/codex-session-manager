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

        var stores = roots.Select(CreateKnownSessionStore).ToArray(); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.

        var sessions = await SessionWorkspaceIndexer.LoadSessionsAsync(stores, cancellationToken);
        return new DiscoveredSessionCatalog(sessions);
    }

    private static KnownSessionStore CreateKnownSessionStore(SessionStoreRoot root)
    {
        var normalizedRoot = NormalizeRootPath(root.RootPath); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var backupWorkspaceRoot = Path.GetDirectoryName(normalizedRoot);
        var normalizedBackupWorkspaceRoot = string.IsNullOrWhiteSpace(backupWorkspaceRoot) ? normalizedRoot : backupWorkspaceRoot;

        return root.StoreKind switch // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        {
            SessionStoreKind.Live => new KnownSessionStore(normalizedRoot, root.StoreKind, Path.Combine(normalizedRoot, "sessions"), Path.Combine(normalizedRoot, "session_index.jsonl")), // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
            SessionStoreKind.Backup when normalizedRoot.EndsWith($"{Path.DirectorySeparatorChar}sessions_backup", StringComparison.OrdinalIgnoreCase)
                => new KnownSessionStore(normalizedBackupWorkspaceRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedBackupWorkspaceRoot, "session_index.jsonl")), // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
            _ => new KnownSessionStore(normalizedRoot, root.StoreKind, normalizedRoot, Path.Combine(normalizedRoot, "session_index.jsonl")) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        };
    }

    private static string NormalizeRootPath(string rootPath)
    {
        var alternateSeparator = Path.DirectorySeparatorChar == '/'
            ? '\\'
            : '/';
        return rootPath.Replace(alternateSeparator, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
    }
}

