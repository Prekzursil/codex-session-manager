#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using CodexSessionManager.App.ViewModels;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task RefreshAsync_LoadsSessionsAndSelectsFirstSessionAsync()
    {
        var service = new FakeSessionBrowserService(
            sessions:
            [
                BuildSession("session-1", "Renderer work", "Readable transcript A"),
                BuildSession("session-2", "Maintenance", "Readable transcript B")
            ]);

        var viewModel = new MainWindowViewModel(service);

        await viewModel.RefreshAsync();

        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("session-1", viewModel.SelectedSession?.SessionId);
        Assert.Contains("Readable transcript A", viewModel.TranscriptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_WithNoSessions_ClearsSelectionAndTranscriptAsync()
    {
        var viewModel = new MainWindowViewModel(new FakeSessionBrowserService([]));

        await viewModel.RefreshAsync();

        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Empty(viewModel.Sessions);
        Assert.Null(viewModel.SelectedSession);
        Assert.Equal(string.Empty, viewModel.TranscriptText);
    }

    [Fact]
    public async Task ApplySearchAsync_UsesSearchHitsToFilterVisibleSessionsAsync()
    {
        var sessions = new[]
        {
            BuildSession("session-1", "Renderer work", "Readable transcript A"),
            BuildSession("session-2", "Maintenance", "Readable transcript B")
        };
        var service = new FakeSessionBrowserService(
            sessions,
            searchHits:
            [
                new SessionSearchHit("session-2", "Maintenance", @"C:\sessions\session-2.jsonl", "Maintenance snippet", 1)
            ]);

        var viewModel = new MainWindowViewModel(service);
        await viewModel.RefreshAsync();

        await viewModel.ApplySearchAsync("maint");

        Assert.Single(viewModel.Sessions);
        Assert.Equal("session-2", viewModel.Sessions[0].SessionId);
        Assert.Equal("session-2", viewModel.SelectedSession?.SessionId);
    }

    [Fact]
    public async Task ApplySearchAsync_WithBlankQuery_RestoresAllSessionsAsync()
    {
        var sessions = new[]
        {
            BuildSession("session-1", "Renderer work", "Readable transcript A"),
            BuildSession("session-2", "Maintenance", "Readable transcript B")
        };
        var service = new FakeSessionBrowserService(
            sessions,
            searchHits:
            [
                new SessionSearchHit("session-2", "Maintenance", @"C:\sessions\session-2.jsonl", "Maintenance snippet", 1)
            ]);

        var viewModel = new MainWindowViewModel(service);
        await viewModel.RefreshAsync();
        await viewModel.ApplySearchAsync("maint");

        await viewModel.ApplySearchAsync("   ");

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("session-1", viewModel.SelectedSession?.SessionId);
        Assert.Contains("Readable transcript A", viewModel.TranscriptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplySearchAsync_WithNullQuery_RestoresAllSessionsAsync()
    {
        var sessions = new[]
        {
            BuildSession("session-1", "Renderer work", "Readable transcript A"),
            BuildSession("session-2", "Maintenance", "Readable transcript B")
        };
        var service = new FakeSessionBrowserService(
            sessions,
            searchHits:
            [
                new SessionSearchHit("session-2", "Maintenance", @"C:\sessions\session-2.jsonl", "Maintenance snippet", 1)
            ]);

        var viewModel = new MainWindowViewModel(service);
        await viewModel.RefreshAsync();
        await viewModel.ApplySearchAsync("maint");

        await viewModel.ApplySearchAsync(null!);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("session-1", viewModel.SelectedSession?.SessionId);
        Assert.Contains("Readable transcript A", viewModel.TranscriptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplySearchAsync_WithNoHits_clears_selection_and_transcriptAsync()
    {
        var sessions = new[]
        {
            BuildSession("session-1", "Renderer work", "Readable transcript A"),
            BuildSession("session-2", "Maintenance", "Readable transcript B")
        };
        var service = new FakeSessionBrowserService(sessions, searchHits: []);

        var viewModel = new MainWindowViewModel(service);
        await viewModel.RefreshAsync();

        await viewModel.ApplySearchAsync("missing");

        Assert.Empty(viewModel.Sessions);
        Assert.Null(viewModel.SelectedSession);
        Assert.Equal(string.Empty, viewModel.TranscriptText);
    }

    [Fact]
    public async Task ApplySearchAsync_single_parameter_overload_normalizes_null_queryAsync()
    {
        var sessions = new[]
        {
            BuildSession("session-1", "Renderer work", "Readable transcript A"),
            BuildSession("session-2", "Maintenance", "Readable transcript B")
        };
        var service = new FakeSessionBrowserService(sessions, searchHits: []);

        var viewModel = new MainWindowViewModel(service);
        await viewModel.RefreshAsync();

        await viewModel.ApplySearchAsync(null!);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("session-1", viewModel.SelectedSession?.SessionId);
        Assert.Contains("Readable transcript A", viewModel.TranscriptText, StringComparison.Ordinal);
    }

    private static IndexedLogicalSession BuildSession(string sessionId, string threadName, string transcript) =>
        new(
            SessionId: sessionId,
            ThreadName: threadName,
            PreferredCopy: new SessionPhysicalCopy(sessionId, $@"C:\Users\Prekzursil\.codex\sessions\{sessionId}.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false)),
            PhysicalCopies:
            [
                new SessionPhysicalCopy(sessionId, $@"C:\Users\Prekzursil\.codex\sessions\{sessionId}.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false))
            ],
            SearchDocument: new SessionSearchDocument
            {
                ReadableTranscript = transcript,
                DialogueTranscript = transcript,
                ToolSummary = "tool summary",
                CommandText = "rg command",
                FilePaths = [],
                Urls = [],
                ErrorText = string.Empty,
                Alias = string.Empty,
                Tags = [],
                Notes = string.Empty
            });

    private sealed class FakeSessionBrowserService : CodexSessionManager.App.ViewModels.ISessionBrowserService
    {
        private readonly IReadOnlyList<IndexedLogicalSession> _sessions;
        private readonly IReadOnlyList<SessionSearchHit>? _searchHits;

        public FakeSessionBrowserService(
            IReadOnlyList<IndexedLogicalSession> sessions,
            IReadOnlyList<SessionSearchHit>? searchHits = null)
        {
            _sessions = sessions;
            _searchHits = searchHits;
        }

        public Task<IReadOnlyList<IndexedLogicalSession>> GetSessionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_sessions);
        }

        public Task<IReadOnlyList<SessionSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            _ = query;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_searchHits ?? []);
        }

        public Task RefreshIndexAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}

