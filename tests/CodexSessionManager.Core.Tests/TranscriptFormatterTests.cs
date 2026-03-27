#pragma warning disable S3990
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Core.Tests;

public sealed class TranscriptFormatterTests
{
    [Fact]
    public void FormatReadable_HidesDeveloperContent_AndSummarizesToolActivity()
    {
        var session = new NormalizedSessionDocument(
            SessionId: "session-1",
            ThreadName: "Implement native helper bridge",
            StartedAtUtc: new DateTimeOffset(2026, 3, 23, 0, 17, 23, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                NormalizedSessionEvent.CreateMessage(SessionActor.Developer, "Developer prompt should be omitted"),
                NormalizedSessionEvent.CreateMessage(SessionActor.User, "Please inspect the session renderer"),
                NormalizedSessionEvent.CreateToolCall("exec_command", """{"cmd":"Get-Content renderer.mjs"}"""),
                NormalizedSessionEvent.CreateToolOutput("exec_command", "Process exited with code 0"),
                NormalizedSessionEvent.CreateMessage(SessionActor.Assistant, "I found the renderer logic.")
            ]);

        var result = SessionTranscriptFormatter.Format(session, TranscriptMode.Readable);

        Assert.DoesNotContain("Developer prompt should be omitted", result.RenderedMarkdown);
        Assert.Contains("Please inspect the session renderer", result.RenderedMarkdown);
        Assert.Contains("I found the renderer logic.", result.RenderedMarkdown);
        Assert.Contains("Tool Activity", result.RenderedMarkdown);
        Assert.Contains("exec_command", result.RenderedMarkdown);
    }
}

