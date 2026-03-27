#pragma warning disable S3990
namespace CodexSessionManager.Core.Transcripts;

public sealed record TranscriptRenderResult(
    TranscriptMode Mode,
    string RenderedMarkdown);

