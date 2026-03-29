#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Parsing;

public sealed record ParsedSessionFile(
    string SessionId,
    string? ForkedFromId,
    string? Cwd,
    TechnicalBreadcrumbs TechnicalBreadcrumbs,
    NormalizedSessionDocument Document);

