using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionSearchHit(
    string SessionId,
    string ThreadName,
    string PreferredPath,
    string Snippet,
    double Score);

