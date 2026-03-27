// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Diagnostics.CodeAnalysis; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

namespace CodexSessionManager.Core.Transcripts;

[ExcludeFromCodeCoverage]
public sealed record NormalizedSessionDocument(
    string SessionId,
    string? ThreadName,
    DateTimeOffset StartedAtUtc,
    string? ForkedFromId,
    string? Cwd,
    IReadOnlyList<NormalizedSessionEvent> Events);

