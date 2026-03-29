#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

public sealed record SessionDiscoveryCatalog(
    IReadOnlyList<LogicalSession> LogicalSessions);

