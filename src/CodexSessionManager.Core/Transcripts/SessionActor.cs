#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Transcripts;

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

