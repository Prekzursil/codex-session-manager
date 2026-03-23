namespace CodexSessionManager.Core.Sessions;

public sealed record IndexedLogicalSession(
    string SessionId,
    string ThreadName,
    SessionPhysicalCopy PreferredCopy,
    IReadOnlyList<SessionPhysicalCopy> PhysicalCopies,
    SessionSearchDocument SearchDocument);
