namespace CodexSessionManager.Core.Maintenance;

public sealed record MaintenanceWarning(
    MaintenanceWarningSeverity Severity,
    string Message);
