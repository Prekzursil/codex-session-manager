#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Maintenance;

public enum MaintenanceWarningSeverity
{
    Info = 0,
    Review = 1,
    Dangerous = 2
}

