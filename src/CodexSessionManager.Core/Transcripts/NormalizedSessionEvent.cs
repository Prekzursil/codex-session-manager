#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Transcripts;

[ExcludeFromCodeCoverage]
public sealed record NormalizedSessionEvent
{
    public NormalizedSessionEvent(
        NormalizedEventKind kind,
        SessionActor actor,
        string text)
        : this(kind, actor, text, string.Empty, string.Empty)
    {
    }

    public NormalizedSessionEvent(
        NormalizedEventKind kind,
        SessionActor actor,
        string text,
        string toolName,
        string rawPayload)
    {
        Kind = kind;
        Actor = actor;
        Text = text ?? string.Empty;
        ToolName = toolName ?? string.Empty;
        RawPayload = rawPayload ?? string.Empty;
    }

    public NormalizedEventKind Kind { get; init; }

    public SessionActor Actor { get; init; }

    public string Text { get; init; }

    public string ToolName { get; init; }

    public string RawPayload { get; init; }

    public static NormalizedSessionEvent CreateMessage(SessionActor actor, string text) =>
        new(NormalizedEventKind.Message, actor, text);

    public static NormalizedSessionEvent CreateToolCall(string toolName, string rawPayload) =>
        new(NormalizedEventKind.ToolCall, SessionActor.Tool, toolName, toolName, rawPayload);

    public static NormalizedSessionEvent CreateToolOutput(string toolName, string text) =>
        new(NormalizedEventKind.ToolOutput, SessionActor.Tool, text, toolName, text);
}

