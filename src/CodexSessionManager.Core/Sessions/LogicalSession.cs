namespace CodexSessionManager.Core.Sessions;

public sealed record LogicalSession(
    string SessionId,
    string? ThreadName,
    SessionPhysicalCopy PreferredCopy,
    IReadOnlyList<SessionPhysicalCopy> PhysicalCopies);

