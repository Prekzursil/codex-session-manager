using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Indexing;

namespace CodexSessionManager.Storage.Tests;

public sealed class SessionMetadataRepositoryTests
{
    [Fact]
    public async Task UpdateMetadataAsync_PersistsAliasTagsNotes_AndMakesThemSearchableAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);

            var session = new IndexedLogicalSession(
                "session-1",
                "Renderer work",
                new SessionPhysicalCopy("session-1", @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero), 1000, false)),
                [
                    new SessionPhysicalCopy("session-1", @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero), 1000, false))
                ],
                new SessionSearchDocument
                {
                    ReadableTranscript = "renderer transcript",
                    DialogueTranscript = "renderer transcript",
                    ToolSummary = "tool summary",
                    CommandText = "rg renderer",
                    FilePaths = [],
                    Urls = [],
                    ErrorText = "",
                    Alias = "",
                    Tags = [],
                    Notes = ""
                });

            await repository.UpsertAsync(session, CancellationToken.None);
            await repository.UpdateMetadataAsync("session-1", "Pinned session", ["important", "renderer"], "Keep for regression checks", CancellationToken.None);

            var hits = await repository.SearchAsync("Pinned session", CancellationToken.None);
            var sessions = await repository.ListSessionsAsync(CancellationToken.None);

            Assert.Contains(hits, hit => hit.SessionId == "session-1");
            var stored = Assert.Single(sessions);
            Assert.Equal("Pinned session", stored.SearchDocument.Alias);
            Assert.Equal(2, stored.SearchDocument.Tags.Count);
            Assert.Contains("regression", stored.SearchDocument.Notes, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

