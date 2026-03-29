#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;

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

    private async Task PopulateSelectedSessionHeaderAsync(IndexedLogicalSession selected, string? selectedSessionId)
    {
        var selectedSession = selected ?? throw new ArgumentNullException(nameof(selected));
        var sessionId = RequireSelectedSessionId(selectedSessionId);

        var preferredCopy = selectedSession.PreferredCopy;
        if (preferredCopy is null)
        {
            throw new InvalidOperationException("Selected session is missing a preferred copy.");
        }

        var searchDocument = selectedSession.SearchDocument;
        if (searchDocument is null)
        {
            throw new InvalidOperationException("Selected session is missing search metadata.");
        }

        var physicalCopies = selectedSession.PhysicalCopies ?? Array.Empty<SessionPhysicalCopy>();
        var threadName = selectedSession.ThreadName;
        var sessionId = selectedSession.SessionId;

        await RunOnUiThreadAsync(() =>
        {
            if (!string.Equals(GetSelectedSession()?.SessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            ThreadNameTextBlock.Text = threadName;
            SessionIdTextBlock.Text = sessionId;
            PreferredPathTextBlock.Text = preferredCopy.FilePath;
            AliasTextBox.Text = searchDocument.Alias;
            TagsTextBox.Text = string.Join(", ", searchDocument.Tags);
            NotesTextBox.Text = searchDocument.Notes;
            CopiesListBox.ItemsSource = physicalCopies;
            ReadableTranscriptTextBox.Text = searchDocument.ReadableTranscript;
            DialogueTranscriptTextBox.Text = searchDocument.DialogueTranscript;
        });
    }

    private async Task LoadSelectedSessionBodyAsync(IndexedLogicalSession selected, string? selectedSessionId)
    {
        var selectedSession = selected ?? throw new ArgumentNullException(nameof(selected));
        var sessionId = RequireSelectedSessionId(selectedSessionId);

        try
        {
            var preferredCopy = selectedSession.PreferredCopy;
            if (preferredCopy is null)
            {
                throw new InvalidOperationException("Selected session is missing a preferred copy.");
            }

            var preferredPath = preferredCopy.FilePath;
            var parsed = await SessionParser(preferredPath, CancellationToken.None);
            var rawContent = FileTextReader(preferredPath);
            var readableTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Readable).RenderedMarkdown;
            var dialogueTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Dialogue).RenderedMarkdown;
            var auditTranscript = SessionTranscriptFormatter.Format(parsed.Document, TranscriptMode.Audit).RenderedMarkdown;
            var sqliteStatus = LiveSqliteStatusProvider();

            if (await IsSessionStillSelectedAsync(sessionId))
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
            if (await IsSessionStillSelectedAsync(sessionId))
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
        var repository = _repository;
        if (repository is null)
        {
            return;
        }

        var sessions = await repository.ListSessionsAsync(CancellationToken.None);
        var searchCanceled = IsSearchCanceled(searchToken);
        await RunOnUiThreadAsync(() =>
        {
            if (searchCanceled)
            {
                return;
            }

            _sessions.Clear();
            foreach (var session in sessions)
            {
                _sessions.Add(session);
            }

            StatusTextBlock.Text = $"Loaded {_sessions.Count} sessions from cached index.";
        });
    }

    private async Task ApplySearchResultsAsync(string query, CancellationToken searchToken)
    {
        var repository = _repository;
        if (repository is null)
        {
            throw new InvalidOperationException("Repository has not been initialized.");
        }

        var searchQuery = query;
        if (searchQuery is null)
        {
            searchQuery = string.Empty;
        }

        var hits = await repository.SearchAsync(searchQuery, CancellationToken.None);
        var hitIds = hits.Select(hit => hit.SessionId).ToHashSet(StringComparer.Ordinal);
        var allSessions = await repository.ListSessionsAsync(CancellationToken.None);
        var visibleSessions = allSessions.Where(session => hitIds.Contains(session.SessionId)).ToArray();
        var searchCanceled = IsSearchCanceled(searchToken);

        await RunOnUiThreadAsync(() =>
        {
            if (searchCanceled)
            {
                return;
            }

            _sessions.Clear();
            foreach (var session in visibleSessions)
            {
                _sessions.Add(session);
            }

            StatusTextBlock.Text = $"Search returned {_sessions.Count} sessions.";
        });
    }

    private CancellationToken BeginSearchToken()
    {
        return _searchCancellation.Begin();
    }

    private static string RequireSelectedSessionId(string? selectedSessionId)
    {
        var sessionId = selectedSessionId ?? throw new ArgumentNullException(nameof(selectedSessionId));
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(selectedSessionId));
        }

        return sessionId;
    }

    private static bool IsSearchCanceled(CancellationToken searchToken) => searchToken.IsCancellationRequested;

    private Task<bool> IsSessionStillSelectedAsync(string sessionId) =>
        RunOnUiThreadValueAsync(() =>
            string.Equals(GetSelectedSession()?.SessionId, sessionId, StringComparison.Ordinal));

    private void ReleaseSearchCancellationState()
    {
        _searchCancellation.Dispose();
    }
}
