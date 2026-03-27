#pragma warning disable S3990
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public sealed record SessionDiscoveryCatalog(
    IReadOnlyList<LogicalSession> LogicalSessions);

