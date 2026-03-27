using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Core.Tests;

public sealed class CoreCarrierInvariantTests
{
    [Fact]
    public void SessionSearchDocument_normalizes_null_text_and_collections()
    {
        var document = new SessionSearchDocument(
            ReadableTranscript: null!,
            DialogueTranscript: null!,
            ToolSummary: null!,
            CommandText: null!,
            FilePaths: null!,
            Urls: null!,
            ErrorText: null!,
            Alias: null!,
            Tags: null!,
            Notes: null!);

        Assert.Equal(string.Empty, document.ReadableTranscript);
        Assert.Equal(string.Empty, document.DialogueTranscript);
        Assert.Empty(document.FilePaths);
        Assert.Empty(document.Urls);
        Assert.Empty(document.Tags);
        Assert.Equal(string.Empty, document.CombinedText);
    }

    [Fact]
    public void IndexedLogicalSession_throws_when_required_reference_members_are_null()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexedLogicalSession("session", "thread", null!, [], new SessionSearchDocument("", "", "", "", [], [], "", "", [], "")));
        Assert.Throws<ArgumentNullException>(() => new IndexedLogicalSession("session", "thread", BuildCopy(), null!, new SessionSearchDocument("", "", "", "", [], [], "", "", [], "")));
        Assert.Throws<ArgumentNullException>(() => new IndexedLogicalSession("session", "thread", BuildCopy(), [BuildCopy()], null!));
    }

    [Fact]
    public void SessionPhysicalCopy_throws_when_required_strings_are_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionPhysicalCopy(null!, @"C:\tmp\session.jsonl", SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false)));
        Assert.Throws<ArgumentNullException>(() => new SessionPhysicalCopy("session", null!, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false)));
    }

    [Fact]
    public void NormalizedSessionEvent_normalizes_optional_strings()
    {
        var item = new NormalizedSessionEvent(NormalizedEventKind.Note, SessionActor.Unknown, null!, null!, null!);

        Assert.Equal(string.Empty, item.Text);
        Assert.Equal(string.Empty, item.ToolName);
        Assert.Equal(string.Empty, item.RawPayload);
    }

    [Fact]
    public void MaintenanceRequest_and_preview_require_non_null_reference_members()
    {
        Assert.Throws<ArgumentNullException>(() => new MaintenanceRequest(MaintenanceAction.Archive, null!, "ARCHIVE 1 FILE"));
        Assert.Throws<ArgumentNullException>(() => new MaintenanceRequest(MaintenanceAction.Archive, [BuildCopy()], null!));

        Assert.Throws<ArgumentNullException>(() => new MaintenancePreview(MaintenanceAction.Archive, null!, [], [], true, true, "ARCHIVE 1 FILE"));
        Assert.Throws<ArgumentNullException>(() => new MaintenancePreview(MaintenanceAction.Archive, [], null!, [], true, true, "ARCHIVE 1 FILE"));
        Assert.Throws<ArgumentNullException>(() => new MaintenancePreview(MaintenanceAction.Archive, [], [], null!, true, true, "ARCHIVE 1 FILE"));
        Assert.Throws<ArgumentNullException>(() => new MaintenancePreview(MaintenanceAction.Archive, [], [], [], true, true, null!));
    }

    private static SessionPhysicalCopy BuildCopy() =>
        new("session", @"C:\tmp\session.jsonl", SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false));
}
