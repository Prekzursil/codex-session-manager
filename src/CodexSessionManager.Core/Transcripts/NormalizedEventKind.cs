#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Transcripts;

public enum NormalizedEventKind
{
    Message = 0,
    ToolCall = 1,
    ToolOutput = 2,
    Note = 3
}

