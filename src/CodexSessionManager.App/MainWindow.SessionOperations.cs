using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.App;

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
        await RunOnUiThreadAsync(() =>
        {
            if (string.Equals(GetSelectedSession()?.SessionId, selectedSessionId, StringComparison.Ordinal))
            {
                ThreadNameTextBlock.Text = selected.ThreadName;
                SessionIdTextBlock.Text = selected.SessionId;
                PreferredPathTextBlock.Text = selected.PreferredCopy.FilePath;
                AliasTextBox.Text = selected.SearchDocument.Alias;
                TagsTextBox.Text = string.Join(", ", selected.SearchDocument.Tags);
                NotesTextBox.Text = selected.SearchDocument.Notes;
                CopiesListBox.ItemsSource = selected.PhysicalCopies;
                ReadableTranscriptTextBox.Text = selected.SearchDocument.ReadableTranscript;
                DialogueTranscriptTextBox.Text = selected.SearchDocument.DialogueTranscript;
            }
        });
    }

    private async Task LoadSelectedSessionBodyAsync(IndexedLogicalSession selected, string selectedSessionId)
    {
        try
        {
            var parsed = await SessionParser(selected.PreferredCopy.FilePath, CancellationToken.None);
            var rawContent = FileTextReader(selected.PreferredCopy.FilePath);
            var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            var auditTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            var sqliteStatus = LiveSqliteStatusProvider();

            if (await IsSessionStillSelectedAsync(selectedSessionId))
            {
                await RunOnUiThreadAsync(() =>
                {
                    if (string.Equals(GetSelectedSession()?.SessionId, selectedSessionId, StringComparison.Ordinal))
                    {
                        SQLiteStatusTextBlock.Text = sqliteStatus;
                        CwdTextBlock.Text = parsed.Cwd ?? "-";
                        RawTranscriptTextBox.Text = rawContent;
                        ReadableTranscriptTextBox.Text = readableTranscript;
                        DialogueTranscriptTextBox.Text = dialogueTranscript;
                        AuditTranscriptTextBox.Text = auditTranscript;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (await IsSessionStillSelectedAsync(selectedSessionId))
            {
                await RunOnUiThreadAsync(() =>
                {
                    if (string.Equals(GetSelectedSession()?.SessionId, selectedSessionId, StringComparison.Ordinal))
                    {
                        CwdTextBlock.Text = "-";
                        SQLiteStatusTextBlock.Text = "Live SQLite status unavailable.";
                        AuditTranscriptTextBox.Text = string.Empty;
                        RawTranscriptTextBox.Text = $"Unable to load raw session content.{Environment.NewLine}{ex.Message}";
                    }
                });
            }
        }
    }

    private async Task SearchSessionsAsync()
    {
        if (_repository is null)
        {
            return;
        }

        var searchToken = BeginSearchToken();
        var query = await RunOnUiThreadValueAsync(() => SearchTextBox.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            await ReloadSessionsForSearchAsync(searchToken);
        }
        else
        {
            await ApplySearchResultsAsync(query, searchToken);
        }
    }

    private async Task ReloadSessionsForSearchAsync(CancellationToken searchToken)
    {
        var sessions = await _repository!.ListSessionsAsync(CancellationToken.None);
        await RunOnUiThreadAsync(() =>
        {
            if (!searchToken.IsCancellationRequested)
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
        var hits = await _repository!.SearchAsync(query, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await _repository.ListSessionsAsync(CancellationToken.None);
        var visibleSessions = allSessions.Where(session => hitIds.Contains(session.SessionId)).ToArray();

        await RunOnUiThreadAsync(() =>
        {
            if (!searchToken.IsCancellationRequested)
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
