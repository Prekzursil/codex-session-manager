#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionSearchHit(
    string SessionId,
    string ThreadName,
    string PreferredPath,
    string Snippet,
    double Score);

