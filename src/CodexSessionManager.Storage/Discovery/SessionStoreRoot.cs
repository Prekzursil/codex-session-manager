using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public sealed record SessionStoreRoot(
    string RootPath,
    SessionStoreKind StoreKind);

