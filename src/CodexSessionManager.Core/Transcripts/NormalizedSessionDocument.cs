namespace CodexSessionManager.Core.Transcripts;

public sealed record NormalizedSessionDocument(
    string SessionId,
    string? ThreadName,
    DateTimeOffset StartedAtUtc,
    string? ForkedFromId,
    string? Cwd,
    IReadOnlyList<NormalizedSessionEvent> Events);
