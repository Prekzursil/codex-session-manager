using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public sealed record DiscoveredSessionCatalog(
    IReadOnlyList<IndexedLogicalSession> LogicalSessions);
