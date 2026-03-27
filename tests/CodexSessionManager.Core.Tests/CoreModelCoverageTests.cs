using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Core.Tests;

public sealed class CoreModelCoverageTests
{
    [Fact]
    public void SessionSearchDocument_CombinedText_JoinsAllNonEmptySegments()
    {
        var document = new SessionSearchDocument
        {
            ReadableTranscript = "Readable transcript",
            DialogueTranscript = "Dialogue transcript",
            ToolSummary = "Tool summary",
            CommandText = "dotnet test",
            FilePaths = ["src/File.cs", "tests/FileTests.cs"],
            Urls = ["https://example.com/doc"],
            ErrorText = "",
            Alias = "Session Alias",
            Tags = ["ops", "wpf"],
            Notes = "Session note"
        };

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
        var fixture = CreateMaintenanceAndSessionFixture();

        AssertCoreRecordValues(fixture);
        AssertDeconstructionValues(fixture);
        AssertUpdatedCopyValues(fixture.Copy);
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

        Assert.Equal(startedAt, document.StartedAtUtc);
        Assert.Equal("parent-1", document.ForkedFromId);
        Assert.Equal(@"C:\Users\Prekzursil\codex-session-manager", document.Cwd);
        Assert.Equal(events, document.Events);
        Assert.Equal(document, document with { });
        var (sessionId, threadName, timestamp, forkedFromId, cwd, renderedEvents) = document;
        Assert.Equal("session-meta", sessionId);
        Assert.Equal("Metadata thread", threadName);
        Assert.Equal(startedAt, timestamp);
        Assert.Equal("parent-1", forkedFromId);
        Assert.Equal(@"C:\Users\Prekzursil\codex-session-manager", cwd);
        Assert.Equal(events, renderedEvents);
        Assert.Equal(TranscriptMode.Audit, renderResult.Mode);
        Assert.Equal("# Markdown", renderResult.RenderedMarkdown);
        Assert.Contains("TranscriptRenderResult", renderResult.ToString());
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

    [Fact]
    public void FormatAudit_RendersNotes_AndSkipsUnknownEventKinds()
    {
        var session = new NormalizedSessionDocument(
            SessionId: "session-note",
            ThreadName: "Note thread",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 15, 0, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                new NormalizedSessionEvent(NormalizedEventKind.Note, SessionActor.Note, "Remember this"),
                new NormalizedSessionEvent((NormalizedEventKind)999, SessionActor.Unknown, "Ignored event")
            ]);

        var audit = SessionTranscriptFormatter.Format(session, TranscriptMode.Audit);

        Assert.Contains("- Note: Remember this", audit.RenderedMarkdown);
        Assert.DoesNotContain("Ignored event", audit.RenderedMarkdown);
    }

    [Fact]
    public void FormatAudit_UsesFallbackToolName_AndTruncatesLongToolOutput()
    {
        var session = new NormalizedSessionDocument(
            SessionId: "session-tool-output",
            ThreadName: "Tool output",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 16, 0, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                NormalizedSessionEvent.CreateToolOutput(string.Empty, new string('x', 200))
            ]);

        var audit = SessionTranscriptFormatter.Format(session, TranscriptMode.Audit);

        Assert.Contains("`tool` output:", audit.RenderedMarkdown);
        Assert.Contains("...", audit.RenderedMarkdown);
    }

    [Fact]
    public void FormatAudit_UsesProvidedToolName_AndKeepsShortOutput()
    {
        var session = new NormalizedSessionDocument(
            SessionId: "session-tool-output-short",
            ThreadName: "Tool output short",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 16, 5, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                NormalizedSessionEvent.CreateToolOutput("exec_command", "short output")
            ]);

        var audit = SessionTranscriptFormatter.Format(session, TranscriptMode.Audit);

        Assert.Contains("`exec_command` output: short output", audit.RenderedMarkdown);
        Assert.DoesNotContain("...", audit.RenderedMarkdown);
    }

    [Fact]
    public void FormatAudit_handles_null_event_collections_and_optional_event_payloads()
    {
        var emptySession = new NormalizedSessionDocument(
            SessionId: "session-empty-events",
            ThreadName: "Empty events",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 16, 10, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events: []) with
        {
            Events = null!
        };

        var emptyAudit = SessionTranscriptFormatter.Format(emptySession, TranscriptMode.Audit);
        Assert.DoesNotContain("### Tool Activity", emptyAudit.RenderedMarkdown);

        var payloadSession = new NormalizedSessionDocument(
            SessionId: "session-null-payloads",
            ThreadName: "Null payloads",
            StartedAtUtc: new DateTimeOffset(2026, 3, 26, 16, 15, 0, TimeSpan.Zero),
            ForkedFromId: null,
            Cwd: @"C:\Users\Prekzursil",
            Events:
            [
                new NormalizedSessionEvent(NormalizedEventKind.Message, SessionActor.Assistant, "Assistant message") with { Text = null! },
                new NormalizedSessionEvent(NormalizedEventKind.Note, SessionActor.Note, "Note payload") with { Text = null! },
                new NormalizedSessionEvent(NormalizedEventKind.ToolCall, SessionActor.Tool, "Tool call") with { ToolName = null!, RawPayload = null! },
                new NormalizedSessionEvent(NormalizedEventKind.ToolOutput, SessionActor.Tool, "Tool output") with { ToolName = null!, Text = null! }
            ]);

        var audit = SessionTranscriptFormatter.Format(payloadSession, TranscriptMode.Audit);

        Assert.Contains("### Assistant", audit.RenderedMarkdown);
        Assert.Contains("- Note:", audit.RenderedMarkdown);
        Assert.Contains("- Called `tool`.", audit.RenderedMarkdown);
        Assert.Contains("- `tool` output:", audit.RenderedMarkdown);
    }

    private static MaintenanceAndSessionFixture CreateMaintenanceAndSessionFixture()
    {
        var copy = new SessionPhysicalCopy(
            sessionId: "session-42",
            filePath: @"C:\Users\Prekzursil\.codex\sessions\2026\03\26\session-42.jsonl",
            storeKind: SessionStoreKind.Live,
            state: new SessionPhysicalCopyState(
                new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero),
                4096,
                true));

        var logical = new LogicalSession("session-42", "Thread", copy, [copy]);
        var indexed = new IndexedLogicalSession(
            SessionId: logical.SessionId,
            ThreadName: logical.ThreadName ?? string.Empty,
            PreferredCopy: copy,
            PhysicalCopies: [copy],
            SearchDocument: new SessionSearchDocument());
        var searchHit = new SessionSearchHit("session-42", "Thread", copy.FilePath, "snippet", 0.75);
        var warning = new MaintenanceWarning(MaintenanceWarningSeverity.Review, "Needs attention");
        var preview = new MaintenancePreview
        {
            Action = MaintenanceAction.Reconcile,
            AllowedTargets = [copy],
            BlockedTargets = [],
            Warnings = [warning],
            RequiresCheckpoint = true,
            RequiresTypedConfirmation = true,
            RequiredTypedConfirmation = "RECONCILE"
        };
        var request = new MaintenanceRequest(MaintenanceAction.Delete, [copy], "DELETE");

        return new MaintenanceAndSessionFixture(copy, logical, indexed, searchHit, warning, preview, request);
    }

    private static void AssertCoreRecordValues(MaintenanceAndSessionFixture fixture)
    {
        Assert.Equal("session-42", fixture.Copy.SessionId);
        Assert.True(fixture.Copy.IsHot);
        Assert.Equal(fixture.Copy, fixture.Logical.PreferredCopy);
        Assert.Equal(fixture.Logical, fixture.Logical with { });
        Assert.Equal(fixture.Copy.FilePath, fixture.SearchHit.PreferredPath);
        Assert.Equal("snippet", fixture.SearchHit.Snippet);
        Assert.Equal(0.75, fixture.SearchHit.Score);
        Assert.NotEqual(fixture.SearchHit, fixture.SearchHit with { Snippet = "updated" });
        Assert.Equal(fixture.Copy, fixture.Indexed.PreferredCopy);
        Assert.Equal(fixture.Indexed, fixture.Indexed with { });
        Assert.Equal(MaintenanceAction.Reconcile, fixture.Preview.Action);
        Assert.NotEqual(fixture.Preview, fixture.Preview with { RequiredTypedConfirmation = "UPDATED" });
        Assert.Equal(fixture.Copy, Assert.Single(fixture.Preview.AllowedTargets));
        Assert.Empty(fixture.Preview.BlockedTargets);
        Assert.Equal(fixture.Warning, Assert.Single(fixture.Preview.Warnings));
        Assert.True(fixture.Preview.RequiresCheckpoint);
        Assert.True(fixture.Preview.RequiresTypedConfirmation);
        Assert.Equal("RECONCILE", fixture.Preview.RequiredTypedConfirmation);
        Assert.Equal(MaintenanceWarningSeverity.Review, fixture.Warning.Severity);
        Assert.Contains("MaintenanceWarning", fixture.Warning.ToString());
        Assert.Equal("DELETE", fixture.Request.TypedConfirmation);
        Assert.Equal(fixture.Copy, Assert.Single(fixture.Request.Targets));
        Assert.Equal(fixture.Request, fixture.Request with { });
        Assert.Contains("SessionPhysicalCopy", fixture.Copy.ToString());
    }

    private static void AssertDeconstructionValues(MaintenanceAndSessionFixture fixture)
    {
        var (hitSessionId, hitThreadName, hitPreferredPath, hitSnippet, hitScore) = fixture.SearchHit;
        Assert.Equal("session-42", hitSessionId);
        Assert.Equal("Thread", hitThreadName);
        Assert.Equal(fixture.Copy.FilePath, hitPreferredPath);
        Assert.Equal("snippet", hitSnippet);
        Assert.Equal(0.75, hitScore);

        var (indexedSessionId, indexedThreadName, indexedPreferredCopy, indexedCopies, indexedSearchDocument) = fixture.Indexed;
        Assert.Equal("session-42", indexedSessionId);
        Assert.Equal("Thread", indexedThreadName);
        Assert.Equal(fixture.Copy, indexedPreferredCopy);
        Assert.Equal(fixture.Copy, Assert.Single(indexedCopies));
        Assert.Equal(fixture.Indexed.SearchDocument, indexedSearchDocument);
    }

    private static void AssertUpdatedCopyValues(SessionPhysicalCopy copy)
    {
        var updatedCopy = copy with
        {
            SessionId = "session-99",
            FilePath = @"C:\archive\session-99.jsonl",
            StoreKind = SessionStoreKind.Backup,
            LastWriteTimeUtc = copy.LastWriteTimeUtc.AddMinutes(5),
            FileSizeBytes = 8192,
            IsHot = false
        };

        Assert.Equal("session-99", updatedCopy.SessionId);
        Assert.Equal(@"C:\archive\session-99.jsonl", updatedCopy.FilePath);
        Assert.Equal(SessionStoreKind.Backup, updatedCopy.StoreKind);
        Assert.False(updatedCopy.IsHot);
    }

    private readonly record struct MaintenanceAndSessionFixture(
        SessionPhysicalCopy Copy,
        LogicalSession Logical,
        IndexedLogicalSession Indexed,
        SessionSearchHit SearchHit,
        MaintenanceWarning Warning,
        MaintenancePreview Preview,
        MaintenanceRequest Request);
}

