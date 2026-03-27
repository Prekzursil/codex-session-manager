// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Diagnostics.CodeAnalysis; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionSearchHit(
    string SessionId,
    string ThreadName,
    string PreferredPath,
    string Snippet,
    double Score);

