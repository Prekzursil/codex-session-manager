#pragma warning disable S3990
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Transcripts;

[ExcludeFromCodeCoverage]
public sealed record NormalizedSessionDocument(
    string SessionId,
    string? ThreadName,
    DateTimeOffset StartedAtUtc,
    string? ForkedFromId,
    string? Cwd,
    IReadOnlyList<NormalizedSessionEvent> Events);

