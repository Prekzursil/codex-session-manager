// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public sealed record LogicalSession(
    string SessionId,
    string? ThreadName,
    SessionPhysicalCopy PreferredCopy,
    IReadOnlyList<SessionPhysicalCopy> PhysicalCopies);

