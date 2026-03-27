using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Maintenance;

public sealed record MaintenanceExecutionResult(
    bool Executed,
    IReadOnlyList<SessionPhysicalCopy> MovedTargets,
    string ManifestPath);

