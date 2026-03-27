using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Indexing;

namespace CodexSessionManager.App;

[SuppressMessage("Code Smell", "S2333", Justification = "The class is split across XAML-generated and hand-authored partial files.")]
public partial class MainWindow
{
    private async Task LoadSelectedSessionAsync()
    {
        var selected = await RunOnUiThreadValueAsync(GetSelectedSession);
        if (selected is null)
        {
            return;
        }

        var selectedSessionId = selected.SessionId;
        await PopulateSelectedSessionHeaderAsync(selected, selectedSessionId);
        await LoadSelectedSessionBodyAsync(selected, selectedSessionId);
    }

    private async Task PopulateSelectedSessionHeaderAsync(IndexedLogicalSession selected, string selectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var preferredCopy = selected.PreferredCopy;
        if (preferredCopy is null)
        {
            throw new InvalidOperationException("Selected session is missing a preferred copy.");
        }

        var searchDocument = selected.SearchDocument;
        if (searchDocument is null)
        {
            throw new InvalidOperationException("Selected session is missing search metadata.");
        }

        var physicalCopies = selected.PhysicalCopies ?? [];
        var threadName = selected.ThreadName;

        await RunOnUiThreadAsync(() =>
        {
            if (string.Equals(GetSelectedSession()?.SessionId, selectedSessionId, StringComparison.Ordinal))
            {
                ThreadNameTextBlock.Text = threadName;
                SessionIdTextBlock.Text = selectedSessionId;
                PreferredPathTextBlock.Text = preferredCopy.FilePath;
                AliasTextBox.Text = searchDocument.Alias;
                TagsTextBox.Text = string.Join(", ", searchDocument.Tags);
                NotesTextBox.Text = searchDocument.Notes;
                CopiesListBox.ItemsSource = physicalCopies;
                ReadableTranscriptTextBox.Text = searchDocument.ReadableTranscript;
                DialogueTranscriptTextBox.Text = searchDocument.DialogueTranscript;
            }
        });
    }

    private async Task LoadSelectedSessionBodyAsync(IndexedLogicalSession selected, string selectedSessionId)
    {
        ArgumentNullException.ThrowIfNull(selected);

        try
        {
            var preferredCopy = selected.PreferredCopy;
            if (preferredCopy is null)
            {
                throw new InvalidOperationException("Selected session is missing a preferred copy.");
            }

            var preferredPath = preferredCopy.FilePath;
            var parser = SessionParser;
            var fileTextReader = FileTextReader;
            var sqliteStatusProvider = LiveSqliteStatusProvider;

            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(fileTextReader);
            ArgumentNullException.ThrowIfNull(sqliteStatusProvider);

            var parsed = await parser(preferredPath, CancellationToken.None);
            var rawContent = fileTextReader(preferredPath);
            var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            var auditTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            var sqliteStatus = sqliteStatusProvider();

            if (await IsSessionStillSelectedAsync(selectedSessionId))
            {
                await RunOnUiThreadAsync(() =>
                {
                    SQLiteStatusTextBlock.Text = sqliteStatus;
                    CwdTextBlock.Text = parsed.Cwd ?? "-";
                    RawTranscriptTextBox.Text = rawContent;
                    ReadableTranscriptTextBox.Text = readableTranscript;
                    DialogueTranscriptTextBox.Text = dialogueTranscript;
                    AuditTranscriptTextBox.Text = auditTranscript;
                });
            }
        }
        catch (Exception ex)
        {
            if (await IsSessionStillSelectedAsync(selectedSessionId))
            {
                await RunOnUiThreadAsync(() =>
                {
                    CwdTextBlock.Text = "-";
                    SQLiteStatusTextBlock.Text = "Live SQLite status unavailable.";
                    AuditTranscriptTextBox.Text = string.Empty;
                    RawTranscriptTextBox.Text = $"Unable to load raw session content.{Environment.NewLine}{ex.Message}";
                });
            }
        }
    }

    private async Task SearchSessionsAsync()
    {
        var repository = _repository;
        if (repository is null)
        {
            return;
        }

        var searchToken = BeginSearchToken();
        var query = await RunOnUiThreadValueAsync(() => SearchTextBox.Text);
        await ExecuteSearchAsync(repository, query, searchToken);
    }

    private Task ExecuteSearchAsync(SessionCatalogRepository repository, string? query, CancellationToken searchToken) =>
        string.IsNullOrWhiteSpace(query)
            ? ReloadSessionsForSearchAsync(repository, searchToken)
            : ApplySearchResultsAsync(query, repository, searchToken);

    private async Task ReloadSessionsForSearchAsync(SessionCatalogRepository repository, CancellationToken searchToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var sessions = await repository.ListSessionsAsync(CancellationToken.None);
        var searchCanceled = searchToken.CanBeCanceled && searchToken.IsCancellationRequested;
        await RunOnUiThreadAsync(() =>
        {
            if (!searchCanceled)
            {
                _sessions.Clear();
                foreach (var session in sessions)
                {
                    _sessions.Add(session);
                }

                StatusTextBlock.Text = $"Loaded {_sessions.Count} sessions from cached index.";
            }
        });
    }

    private async Task ApplySearchResultsAsync(string query, CancellationToken searchToken)
    {
        var repository = _repository ?? throw new InvalidOperationException("Repository has not been initialized.");
        await ApplySearchResultsAsync(query, repository, searchToken);
    }

    private async Task ApplySearchResultsAsync(string query, SessionCatalogRepository repository, CancellationToken searchToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var searchQuery = query ?? string.Empty;
        var hits = await repository.SearchAsync(searchQuery, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await repository.ListSessionsAsync(CancellationToken.None);
        var visibleSessions = allSessions.Where(session => hitIds.Contains(session.SessionId)).ToArray();
        var searchCanceled = searchToken.CanBeCanceled && searchToken.IsCancellationRequested;

        await RunOnUiThreadAsync(() =>
        {
            if (!searchCanceled)
            {
                _sessions.Clear();
                foreach (var session in visibleSessions)
                {
                    _sessions.Add(session);
                }

                StatusTextBlock.Text = $"Search returned {_sessions.Count} sessions.";
            }
        });
    }

    private CancellationToken BeginSearchToken()
    {
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, replacement);
        previous?.Cancel();
        previous?.Dispose();
        return replacement.Token;
    }

    private async Task<bool> IsSessionStillSelectedAsync(string sessionId)
    {
        return await RunOnUiThreadValueAsync(() =>
            string.Equals(GetSelectedSession()?.SessionId, sessionId, StringComparison.Ordinal));
    }

    private void DisposeSearchCancellation()
    {
        var current = Interlocked.Exchange(ref _searchCts, null);
        current?.Cancel();
        current?.Dispose();
    }
}

