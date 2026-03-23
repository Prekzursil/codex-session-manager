using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Parsing;

public sealed record ParsedSessionFile(
    string SessionId,
    string? ForkedFromId,
    string? Cwd,
    TechnicalBreadcrumbs TechnicalBreadcrumbs,
    NormalizedSessionDocument Document);
