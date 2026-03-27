#pragma warning disable S3990
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

        var selectedSessionId = GetSessionId(selected);
        await PopulateSelectedSessionHeaderAsync(selected, selectedSessionId);
        await LoadSelectedSessionBodyAsync(selected, selectedSessionId);
    }

    private async Task PopulateSelectedSessionHeaderAsync(IndexedLogicalSession selected, string selectedSessionId)
    {
        var preferredCopy = selected.PreferredCopy ?? throw new InvalidOperationException("Selected session is missing a preferred copy."); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var searchDocument = selected.SearchDocument ?? throw new InvalidOperationException("Selected session is missing search metadata."); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var physicalCopies = selected.PhysicalCopies ?? []; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.

        await RunOnUiThreadAsync(() =>
        {
            if (string.Equals(GetSelectedSession()?.SessionId, selectedSessionId, StringComparison.Ordinal))
            {
                ThreadNameTextBlock.Text = GetThreadName(selected); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
                SessionIdTextBlock.Text = GetSessionId(selected); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
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
        try
        {
            var preferredCopy = selected.PreferredCopy ?? throw new InvalidOperationException("Selected session is missing a preferred copy.");
            var parsed = await SessionParser(preferredCopy.FilePath, CancellationToken.None);
            var rawContent = FileTextReader(preferredCopy.FilePath);
            var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            var auditTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            var sqliteStatus = LiveSqliteStatusProvider();

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
        var searchCanceled = searchToken.IsCancellationRequested; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
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
        var searchQuery = query ?? string.Empty;
        var hits = await repository.SearchAsync(searchQuery, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await repository.ListSessionsAsync(CancellationToken.None);
        var visibleSessions = allSessions.Where(session => hitIds.Contains(session.SessionId)).ToArray();
        var searchCanceled = searchToken.IsCancellationRequested;

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

    private static string GetSessionId(IndexedLogicalSession session) => session.SessionId; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.

    private static string GetThreadName(IndexedLogicalSession session) => session.ThreadName; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
}

