using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Core.Tests;

public sealed class CoreModelCoverageTests
{
    [Fact]
    public void SessionSearchDocument_CombinedText_JoinsAllNonEmptySegments()
    {
        var document = new SessionSearchDocument(
            ReadableTranscript: "Readable transcript",
            DialogueTranscript: "Dialogue transcript",
            ToolSummary: "Tool summary",
            CommandText: "dotnet test",
            FilePaths: ["src/File.cs", "tests/FileTests.cs"],
            Urls: ["https://example.com/doc"],
            ErrorText: "",
            Alias: "Session Alias",
            Tags: ["ops", "wpf"],
            Notes: "Session note");

        Assert.Equal(
            string.Join(
                '\n',
                [
                    "Readable transcript",
                    "Dialogue transcript",
                    "Tool summary",
                    "dotnet test",
                    "src/File.cs tests/FileTests.cs",
                    "https://example.com/doc",
                    "Session Alias",
                    "ops wpf",
                    "Session note"
                ]),
            document.CombinedText);
    }

    [Fact]
    public void MaintenanceAndSessionRecords_PreserveConstructorValues()
    {
        var copy = new SessionPhysicalCopy(
            sessionId: "session-42",
            filePath: @"C:\Users\Prekzursil\.codex\sessions\2026\03\26\session-42.jsonl",
            storeKind: SessionStoreKind.Live,
            lastWriteTimeUtc: new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero),
            fileSizeBytes: 4096,
            isHot: true);

        var logical = new LogicalSession("session-42", "Thread", copy, [copy]);
        var indexed = new IndexedLogicalSession(
            SessionId: logical.SessionId,
            ThreadName: logical.ThreadName ?? string.Empty,
            PreferredCopy: copy,
            PhysicalCopies: [copy],
            SearchDocument: new SessionSearchDocument("", "", "", "", [], [], "", "", [], ""));
        var searchHit = new SessionSearchHit("session-42", "Thread", copy.FilePath, "snippet", 0.75);

        var warning = new MaintenanceWarning(MaintenanceWarningSeverity.Review, "Needs attention");
        var preview = new MaintenancePreview(
            Action: MaintenanceAction.Reconcile,
            AllowedTargets: [copy],
            BlockedTargets: [],
            Warnings: [warning],
            RequiresCheckpoint: true,
            RequiresTypedConfirmation: true,
            RequiredTypedConfirmation: "RECONCILE");
        var request = new MaintenanceRequest(MaintenanceAction.Delete, [copy], "DELETE");

        Assert.Equal("session-42", copy.SessionId);
        Assert.Equal(copy.FilePath, copy.FilePath);
        Assert.Equal(SessionStoreKind.Live, copy.StoreKind);
        Assert.Equal(new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero), copy.LastWriteTimeUtc);
        Assert.Equal(4096, copy.FileSizeBytes);
        Assert.True(copy.IsHot);
        Assert.Equal("session-42", logical.SessionId);
        Assert.Equal("Thread", logical.ThreadName);
        Assert.Equal(copy, Assert.Single(logical.PhysicalCopies));
        Assert.Equal(copy, logical.PreferredCopy);
        Assert.Equal("session-42", indexed.SessionId);
        Assert.Equal("Thread", indexed.ThreadName);
        Assert.Equal(copy.FilePath, searchHit.PreferredPath);
        Assert.Equal("session-42", searchHit.SessionId);
        Assert.Equal("Thread", searchHit.ThreadName);
        Assert.Equal("snippet", searchHit.Snippet);
        Assert.Equal(0.75, searchHit.Score);
        Assert.Equal(copy, indexed.PreferredCopy);
        Assert.Equal(copy, Assert.Single(indexed.PhysicalCopies));
        Assert.Equal(string.Empty, indexed.SearchDocument.Notes);
        Assert.Equal(MaintenanceAction.Reconcile, preview.Action);
        Assert.Equal(copy, Assert.Single(preview.AllowedTargets));
        Assert.Empty(preview.BlockedTargets);
        Assert.Equal(warning, Assert.Single(preview.Warnings));
        Assert.True(preview.RequiresCheckpoint);
        Assert.True(preview.RequiresTypedConfirmation);
        Assert.Equal("RECONCILE", preview.RequiredTypedConfirmation);
        Assert.Equal(MaintenanceWarningSeverity.Review, warning.Severity);
        Assert.Equal(MaintenanceAction.Delete, request.Action);
        Assert.Equal("DELETE", request.TypedConfirmation);
        Assert.Equal(copy, Assert.Single(request.Targets));
    }

    [Fact]
    public void TranscriptRecords_PreserveMetadata()
    {
        var startedAt = new DateTimeOffset(2026, 3, 26, 13, 30, 0, TimeSpan.Zero);
        IReadOnlyList<NormalizedSessionEvent> events =
        [
            NormalizedSessionEvent.CreateMessage(SessionActor.User, "Hello"),
            NormalizedSessionEvent.CreateToolCall("exec_command", """{"cmd":"echo hi"}"""),
            NormalizedSessionEvent.CreateToolOutput("exec_command", "hi")
        ];

        var document = new NormalizedSessionDocument(
            SessionId: "session-meta",
            ThreadName: "Metadata thread",
            StartedAtUtc: startedAt,
            ForkedFromId: "parent-1",
            Cwd: @"C:\Users\Prekzursil\codex-session-manager",
            Events: events);
        var renderResult = new TranscriptRenderResult(TranscriptMode.Audit, "# Markdown");

        Assert.Equal("session-meta", document.SessionId);
        Assert.Equal("Metadata thread", document.ThreadName);
        Assert.Equal(startedAt, document.StartedAtUtc);
        Assert.Equal("parent-1", document.ForkedFromId);
        Assert.Equal(@"C:\Users\Prekzursil\codex-session-manager", document.Cwd);
        Assert.Equal(events, document.Events);
        Assert.Equal(TranscriptMode.Audit, renderResult.Mode);
        Assert.Equal("# Markdown", renderResult.RenderedMarkdown);
    }

    [Fact]
    public void FormatDialogue_ExcludesToolActivity_AndAuditKeepsDeveloperMessages()
    {
        var session = new NormalizedSessionDocument(
            SessionId: "session-coverage",
            ThreadName: "Coverage thread",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 14, 0, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                NormalizedSessionEvent.CreateMessage(SessionActor.System, "System context"),
                NormalizedSessionEvent.CreateMessage(SessionActor.Developer, "Developer note"),
                NormalizedSessionEvent.CreateMessage(SessionActor.User, "Please inspect transcripts"),
                NormalizedSessionEvent.CreateMessage(SessionActor.Note, "Internal note"),
                NormalizedSessionEvent.CreateToolCall("exec_command", ""),
                NormalizedSessionEvent.CreateToolOutput("exec_command", "A very long tool output that should be truncated in audit mode because it exceeds the formatter limit.")
            ]);

        var dialogue = SessionTranscriptFormatter.Format(session, TranscriptMode.Dialogue);
        var audit = SessionTranscriptFormatter.Format(session, TranscriptMode.Audit);

        Assert.DoesNotContain("Tool Activity", dialogue.RenderedMarkdown);
        Assert.DoesNotContain("System context", dialogue.RenderedMarkdown);
        Assert.DoesNotContain("Developer note", dialogue.RenderedMarkdown);
        Assert.Contains("Please inspect transcripts", dialogue.RenderedMarkdown);

        Assert.Contains("System context", audit.RenderedMarkdown);
        Assert.Contains("Developer note", audit.RenderedMarkdown);
        Assert.Contains("### Note", audit.RenderedMarkdown);
        Assert.Contains("Called `exec_command`.", audit.RenderedMarkdown);
        Assert.Contains("`exec_command` output:", audit.RenderedMarkdown);
    }
}
