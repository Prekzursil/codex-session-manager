using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionJsonlParserTests
{
    [Fact]
    public async Task ParseAsync_ExtractsSessionMetadata_Messages_AndTechnicalBreadcrumbs()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(tempFile,
        [
            """{"timestamp":"2026-03-23T00:17:23.757Z","type":"session_meta","payload":{"id":"session-1","forked_from_id":"parent-1","timestamp":"2026-03-23T00:17:23.757Z","cwd":"C:\\Users\\Prekzursil","originator":"codex_cli_rs","source":"cli","model_provider":"openai"}}""",
            """{"timestamp":"2026-03-23T00:17:25.000Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"search this phrase"}]}}""",
            """{"timestamp":"2026-03-23T00:17:26.000Z","type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":"{\"cmd\":\"rg -n session renderer\"}","call_id":"call-1"}}""",
            """{"timestamp":"2026-03-23T00:17:27.000Z","type":"response_item","payload":{"type":"function_call_output","call_id":"call-1","output":"Process exited with code 0"}}""",
            """{"timestamp":"2026-03-23T00:17:28.000Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"I found the renderer."}]}}"""
        ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-1", parsed.SessionId);
            Assert.Equal("parent-1", parsed.ForkedFromId);
            Assert.Equal(@"C:\Users\Prekzursil", parsed.Cwd);
            Assert.Contains(parsed.TechnicalBreadcrumbs.Commands, command => command.Contains("rg -n session renderer", StringComparison.Ordinal));
            Assert.Contains(parsed.TechnicalBreadcrumbs.ExitCodes, exitCode => exitCode == 0);
            Assert.Contains(parsed.Document.Events, e => e.Kind == NormalizedEventKind.Message && e.Actor == SessionActor.User);
            Assert.Contains(parsed.Document.Events, e => e.Kind == NormalizedEventKind.Message && e.Actor == SessionActor.Assistant);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
