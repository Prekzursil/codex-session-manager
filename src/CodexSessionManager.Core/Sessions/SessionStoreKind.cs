// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public enum SessionStoreKind
{
    Unknown = 0,
    Live = 1,
    Backup = 2,
    Mirror = 3,
    Other = 4
}

