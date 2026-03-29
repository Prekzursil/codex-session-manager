#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Maintenance;

public sealed record MaintenanceExecutionResult(
    bool Executed,
    IReadOnlyList<SessionPhysicalCopy> MovedTargets,
    string ManifestPath);

