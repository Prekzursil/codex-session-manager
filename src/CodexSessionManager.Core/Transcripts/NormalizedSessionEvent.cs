namespace CodexSessionManager.Core.Transcripts;

public sealed record NormalizedSessionEvent(
    NormalizedEventKind Kind,
    SessionActor Actor,
    string Text,
    string? ToolName = null,
    string? RawPayload = null)
{
    public static NormalizedSessionEvent CreateMessage(SessionActor actor, string text) =>
        new(NormalizedEventKind.Message, actor, text);

    public static NormalizedSessionEvent CreateToolCall(string toolName, string rawPayload) =>
        new(NormalizedEventKind.ToolCall, SessionActor.Tool, toolName, toolName, rawPayload);

    public static NormalizedSessionEvent CreateToolOutput(string toolName, string text) =>
        new(NormalizedEventKind.ToolOutput, SessionActor.Tool, text, toolName, text);
}
