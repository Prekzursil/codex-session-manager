// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Maintenance; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public sealed record MaintenanceWarning(
    MaintenanceWarningSeverity Severity,
    string Message);

