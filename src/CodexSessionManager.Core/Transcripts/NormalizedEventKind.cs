// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Transcripts; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public enum NormalizedEventKind
{
    Message = 0,
    ToolCall = 1,
    ToolOutput = 2,
    Note = 3
}

