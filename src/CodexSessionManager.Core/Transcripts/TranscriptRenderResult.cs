namespace CodexSessionManager.Core.Transcripts;

public sealed record TranscriptRenderResult(
    TranscriptMode Mode,
    string RenderedMarkdown);
