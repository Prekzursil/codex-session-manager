using System.Text;

namespace CodexSessionManager.Core.Transcripts;

public static class SessionTranscriptFormatter
{
    public static TranscriptRenderResult Format(NormalizedSessionDocument session, TranscriptMode mode)
    {
        ArgumentNullException.ThrowIfNull(session);

        var builder = new StringBuilder();
        builder.AppendLine("# Codex Session Transcript");
        builder.AppendLine();
        builder.AppendLine("## Conversation");
        builder.AppendLine();

        var toolActivity = new List<string>();

        foreach (var sessionEvent in session.Events)
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
        ArgumentNullException.ThrowIfNull(builder);

        if (ShouldSkipMessage(sessionEvent, mode))
        {
            return;
        }

        builder.AppendLine(ToMessageHeading(sessionEvent.Actor));
        builder.AppendLine(sessionEvent.Text);
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
            NormalizedEventKind.Note => $"- Note: {Truncate(sessionEvent.Text, 140)}",
            _ => null,
        };
    }

    private static string BuildToolCallDescription(NormalizedSessionEvent sessionEvent)
    {
        if (string.IsNullOrWhiteSpace(sessionEvent.RawPayload))
        {
            return $"- Called `{sessionEvent.ToolName}`.";
        }

        return $"- Called `{sessionEvent.ToolName}` with arguments `{Truncate(sessionEvent.RawPayload, 120)}`.";
    }

    private static string BuildToolOutputDescription(NormalizedSessionEvent sessionEvent)
    {
        var toolName = string.IsNullOrWhiteSpace(sessionEvent.ToolName) ? "tool" : sessionEvent.ToolName;
        return $"- `{toolName}` output: {Truncate(sessionEvent.Text, 140)}";
    }

    private static string Truncate(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}
