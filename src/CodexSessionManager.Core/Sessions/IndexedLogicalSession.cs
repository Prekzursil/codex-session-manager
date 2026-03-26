using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record IndexedLogicalSession(
    string SessionId,
    string ThreadName,
    SessionPhysicalCopy PreferredCopy,
    IReadOnlyList<SessionPhysicalCopy> PhysicalCopies,
    SessionSearchDocument SearchDocument);
