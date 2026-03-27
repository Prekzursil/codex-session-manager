// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Maintenance; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public enum MaintenanceWarningSeverity
{
    Info = 0,
    Review = 1,
    Dangerous = 2
}

