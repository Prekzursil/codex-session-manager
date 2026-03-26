using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

[ExcludeFromCodeCoverage]
public sealed record KnownSessionStore(
    string WorkspaceRoot,
    SessionStoreKind StoreKind,
    string SessionsPath,
    string SessionIndexPath);
