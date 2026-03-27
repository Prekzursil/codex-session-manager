using System.Text;

namespace CodexSessionManager.Core.Transcripts;

public static class SessionTranscriptFormatter
{
    public static TranscriptRenderResult Format(NormalizedSessionDocument session, TranscriptMode mode)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Codex Session Transcript");
        builder.AppendLine();
        builder.AppendLine("## Conversation");
        builder.AppendLine();

        var toolActivity = new List<string>();

        var events = GetEvents(session);
        foreach (var sessionEvent in events)
        {
            if (sessionEvent.Kind is NormalizedEventKind.Message)
            {
                AppendMessage(sessionEvent, mode, builder);
                continue;
            }

            var activityLine = DescribeToolActivity(sessionEvent, mode);
            if (!string.IsNullOrWhiteSpace(activityLine))
            {
                toolActivity.Add(activityLine);
            }
        }

        if (toolActivity.Count > 0 && mode is not TranscriptMode.Dialogue)
        {
            builder.AppendLine("### Tool Activity");
            foreach (var line in toolActivity.Distinct(StringComparer.Ordinal))
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
        }

        return new TranscriptRenderResult(mode, builder.ToString().Trim());
    }

    private static void AppendMessage(NormalizedSessionEvent sessionEvent, TranscriptMode mode, StringBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (ShouldSkipMessage(sessionEvent, mode))
        {
            return;
        }

        builder.AppendLine(ToMessageHeading(sessionEvent.Actor));
        builder.AppendLine(GetEventText(sessionEvent));
        builder.AppendLine();
    }

    private static bool ShouldSkipMessage(NormalizedSessionEvent sessionEvent, TranscriptMode mode)
    {
        return mode is TranscriptMode.Readable or TranscriptMode.Dialogue
            && sessionEvent.Actor is SessionActor.Developer or SessionActor.System;
    }

    private static string ToMessageHeading(SessionActor actor)
    {
        return actor switch
        {
            SessionActor.User => "### User",
            SessionActor.Assistant => "### Assistant",
            SessionActor.Developer => "### Developer",
            SessionActor.System => "### System",
            _ => "### Note",
        };
    }

    private static string? DescribeToolActivity(NormalizedSessionEvent sessionEvent, TranscriptMode mode)
    {
        if (mode is TranscriptMode.Dialogue)
        {
            return null;
        }

        return sessionEvent.Kind switch
        {
            NormalizedEventKind.ToolCall => BuildToolCallDescription(sessionEvent),
            NormalizedEventKind.ToolOutput => BuildToolOutputDescription(sessionEvent),
            NormalizedEventKind.Note => $"- Note: {Truncate(GetEventText(sessionEvent), 140)}",
            _ => null,
        };
    }

    private static string BuildToolCallDescription(NormalizedSessionEvent sessionEvent)
    {
        var toolName = GetToolName(sessionEvent);
        var rawPayload = GetRawPayload(sessionEvent);
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return $"- Called `{toolName}`.";
        }

        return $"- Called `{toolName}` with arguments `{Truncate(rawPayload, 120)}`.";
    }

    private static string BuildToolOutputDescription(NormalizedSessionEvent sessionEvent)
    {
        return $"- `{GetToolName(sessionEvent)}` output: {Truncate(GetEventText(sessionEvent), 140)}";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    private static IReadOnlyList<NormalizedSessionEvent> GetEvents(NormalizedSessionDocument session) =>
        session.Events ?? [];

    private static string GetEventText(NormalizedSessionEvent sessionEvent) =>
        sessionEvent.Text ?? string.Empty;

    private static string GetToolName(NormalizedSessionEvent sessionEvent) =>
        string.IsNullOrWhiteSpace(sessionEvent.ToolName) ? "tool" : sessionEvent.ToolName;

    private static string GetRawPayload(NormalizedSessionEvent sessionEvent) =>
        sessionEvent.RawPayload ?? string.Empty;
}

