#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Maintenance;

public sealed record MaintenanceWarning(
    MaintenanceWarningSeverity Severity,
    string Message);

