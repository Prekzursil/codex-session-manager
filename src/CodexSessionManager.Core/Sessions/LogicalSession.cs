#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Sessions;

public sealed record LogicalSession(
    string SessionId,
    string? ThreadName,
    SessionPhysicalCopy PreferredCopy,
    IReadOnlyList<SessionPhysicalCopy> PhysicalCopies);

