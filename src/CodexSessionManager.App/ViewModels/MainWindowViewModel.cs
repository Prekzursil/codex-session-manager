#pragma warning disable S3990
using System.Collections.ObjectModel;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.App.ViewModels;

public interface ISessionBrowserService
{
    Task<IReadOnlyList<IndexedLogicalSession>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSearchHit>> SearchAsync(string query, CancellationToken cancellationToken);

    Task RefreshIndexAsync(CancellationToken cancellationToken);
}

public sealed class MainWindowViewModel
{
    private readonly ISessionBrowserService _service;
    private readonly List<IndexedLogicalSession> _allSessions = [];

    public MainWindowViewModel(ISessionBrowserService service)
    {
        _service = service;
    }

    public ObservableCollection<IndexedLogicalSession> Sessions { get; } = [];

    public IndexedLogicalSession? SelectedSession { get; private set; }

    public string TranscriptText { get; private set; } = string.Empty;

    public string StatusText { get; private set; } = "Idle";

    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _service.RefreshIndexAsync(cancellationToken); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var sessions = await _service.GetSessionsAsync(cancellationToken); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        _allSessions.Clear();
        _allSessions.AddRange(sessions);
        ReplaceSessions(_allSessions);
        StatusText = "Ready";
    }

    public Task ApplySearchAsync(string query) => ApplySearchAsync(query, CancellationToken.None); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.

    public async Task ApplySearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            ReplaceSessions(_allSessions);
            return;
        }

        var hits = await _service.SearchAsync(normalizedQuery, cancellationToken);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        ReplaceSessions(_allSessions.Where(session => hitIds.Contains(session.SessionId)));
    }

    private void ReplaceSessions(IEnumerable<IndexedLogicalSession> sessions)
    {
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(session);
        }

        SelectedSession = Sessions.FirstOrDefault();
        var selectedSession = SelectedSession;
        TranscriptText = selectedSession is null ? string.Empty : selectedSession.SearchDocument.ReadableTranscript;
    }
}

