namespace CodexSessionManager.Core.Sessions;

public sealed record SessionSearchHit(
    string SessionId,
    string ThreadName,
    string PreferredPath,
    string Snippet,
    double Score);
