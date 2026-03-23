using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public sealed record KnownSessionStore(
    string WorkspaceRoot,
    SessionStoreKind StoreKind,
    string SessionsPath,
    string SessionIndexPath);
