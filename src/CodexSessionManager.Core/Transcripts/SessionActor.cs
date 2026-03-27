// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Transcripts; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public enum SessionActor
{
    Unknown = 0,
    User = 1,
    Assistant = 2,
    Developer = 3,
    System = 4,
    Tool = 5,
    Note = 6
}

