#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Transcripts;

public sealed record TranscriptRenderResult(
    TranscriptMode Mode,
    string RenderedMarkdown);

