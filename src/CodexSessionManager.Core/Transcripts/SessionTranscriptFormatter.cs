using System.Text;

namespace CodexSessionManager.Core.Transcripts;

public static class SessionTranscriptFormatter
{
    public static TranscriptRenderResult Format(NormalizedSessionDocument session, TranscriptMode mode)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Codex Session Transcript");
        builder.AppendLine();
        builder.AppendLine("## Conversation");
        builder.AppendLine();

        var toolActivity = new List<string>();

        foreach (var sessionEvent in session.Events)
        {
            switch (sessionEvent.Kind)
            {
                case NormalizedEventKind.Message:
                    if (mode is TranscriptMode.Readable or TranscriptMode.Dialogue
                        && sessionEvent.Actor is SessionActor.Developer or SessionActor.System)
                    {
                        continue;
                    }

                    builder.AppendLine(sessionEvent.Actor switch
                    {
                        SessionActor.User => "### User",
                        SessionActor.Assistant => "### Assistant",
                        SessionActor.Developer => "### Developer",
                        SessionActor.System => "### System",
                        _ => "### Note"
                    });
                    builder.AppendLine(sessionEvent.Text);
                    builder.AppendLine();
                    break;

                case NormalizedEventKind.ToolCall when mode is not TranscriptMode.Dialogue:
                    toolActivity.Add($"- Called `{sessionEvent.ToolName}`.");
                    if (!string.IsNullOrWhiteSpace(sessionEvent.RawPayload))
                    {
                        toolActivity.Add($"- Arguments: `{Truncate(sessionEvent.RawPayload, 120)}`");
                    }

                    break;

                case NormalizedEventKind.ToolOutput when mode is not TranscriptMode.Dialogue:
                    toolActivity.Add($"- `{sessionEvent.ToolName}` output: {Truncate(sessionEvent.Text, 140)}");
                    break;
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
