using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.Storage.Tests;

public sealed class StorageCoverageExpansionTests
{
    [Fact]
    public void DiscoveryAndCoreRecords_PreserveAssignedValues()
    {
        var preferredCopy = new SessionPhysicalCopy("session-1", @"C:\sessions\session-1.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 128, false));
        var logical = new LogicalSession("session-1", "Thread", preferredCopy, [preferredCopy]);
        var catalog = new SessionDiscoveryCatalog([logical]);
        var knownStore = new KnownSessionStore(
            @"C:\.codex",
            SessionStoreKind.Live,
            @"C:\.codex\sessions",
            @"C:\.codex\session_index.jsonl");
        var searchHit = new SessionSearchHit("session-1", "Thread", preferredCopy.FilePath, "snippet", 0.9);
        var rendered = new TranscriptRenderResult(TranscriptMode.Readable, "markdown");
        var @event = NormalizedSessionEvent.CreateToolOutput("exec_command", "Process exited with code 0");
        var document = new NormalizedSessionDocument(
            "session-1",
            "Thread",
            DateTimeOffset.UtcNow,
            null,
            @"C:\repo",
            [@event]);
        var request = new MaintenanceRequest(MaintenanceAction.Archive, [preferredCopy], "ARCHIVE 1 FILE");

        Assert.Single(catalog.LogicalSessions);
        Assert.Equal(@"C:\.codex", knownStore.WorkspaceRoot);
        Assert.Equal(SessionStoreKind.Live, knownStore.StoreKind);
        Assert.Equal(@"C:\.codex\sessions", knownStore.SessionsPath);
        Assert.Equal(@"C:\.codex\session_index.jsonl", knownStore.SessionIndexPath);
        Assert.Equal("snippet", searchHit.Snippet);
        Assert.Equal(TranscriptMode.Readable, rendered.Mode);
        Assert.Equal(NormalizedEventKind.ToolOutput, document.Events[0].Kind);
        Assert.Equal(MaintenanceAction.Archive, request.Action);
    }

    [Fact]
    public async Task DiscoverAsync_UsesCustomStoreRoots_AndFallsBackWhenThreadIndexMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var customStore = Path.Combine(root, "custom-store");
        var sessionDir = Path.Combine(customStore, "2026", "03", "26");
        Directory.CreateDirectory(sessionDir);

        var sessionPath = Path.Combine(sessionDir, "session-1.jsonl");
        await File.WriteAllLinesAsync(
            sessionPath,
            [
                """{"timestamp":"2026-03-26T10:00:00Z","type":"session_meta","payload":{"id":"session-1","timestamp":"2026-03-26T10:00:00Z","cwd":"C:\\repo"}}""",
                """{"timestamp":"2026-03-26T10:00:01Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"see https://example.com and C:\\repo\\file.txt"}]}}"""
            ]);

        try
        {
            var service = new SessionDiscoveryService();
            var catalog = await service.DiscoverAsync(
                [new SessionStoreRoot(customStore.Replace('\\', '/'), SessionStoreKind.Backup)],
                CancellationToken.None);

            var logical = Assert.Single(catalog.LogicalSessions);
            Assert.Equal("session-1", logical.ThreadName);
            Assert.Equal(SessionStoreKind.Backup, logical.PreferredCopy.StoreKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildAsync_SkipsMissingSessionDirectories_AndBuildsSearchDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var liveRoot = Path.Combine(root, ".codex");
        var liveSessions = Path.Combine(liveRoot, "sessions");
        Directory.CreateDirectory(liveSessions);
        var nestedDir = Path.Combine(liveSessions, "2026", "03", "26");
        Directory.CreateDirectory(nestedDir);

        var sessionPath = Path.Combine(nestedDir, "session-2.jsonl");
        await File.WriteAllLinesAsync(
            sessionPath,
            [
                """{"timestamp":"2026-03-26T10:00:00Z","type":"session_meta","payload":{"id":"session-2","timestamp":"2026-03-26T10:00:00Z","cwd":"C:\\repo"}}""",
                """{"timestamp":"2026-03-26T10:00:01Z","type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":"{\"cmd\":\"pwsh -File C:\\\\repo\\\\scripts\\\\task.ps1 && start https://example.com\"}"}}""",
                """{"timestamp":"2026-03-26T10:00:02Z","type":"response_item","payload":{"type":"function_call_output","name":"exec_command","output":"Process exited with code 7"}}"""
            ]);

        try
        {
            var databasePath = Path.Combine(root, "catalog.db");
            var repository = new SessionCatalogRepository(databasePath);
            await repository.InitializeAsync(CancellationToken.None);
            var indexer = new SessionWorkspaceIndexer(repository);
            var sessions = await indexer.RebuildAsync(
                [
                    new KnownSessionStore(
                        liveRoot,
                        SessionStoreKind.Live,
                        liveSessions,
                        Path.Combine(liveRoot, "session_index.jsonl")),
                    new KnownSessionStore(
                        root,
                        SessionStoreKind.Backup,
                        Path.Combine(root, "missing"),
                        Path.Combine(root, "missing-index.jsonl"))
                ],
                CancellationToken.None);

            var indexed = Assert.Single(sessions);
            Assert.Equal("session-2", indexed.ThreadName);
            Assert.Contains("pwsh -File", indexed.SearchDocument.CommandText);
            Assert.Contains(indexed.SearchDocument.FilePaths, item => item.Contains("task.ps1", StringComparison.Ordinal));
            Assert.Contains("https://example.com", indexed.SearchDocument.Urls);
            Assert.Contains("Exit 7", indexed.SearchDocument.ErrorText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseAsync_ThrowsWhenSessionIdIsMissing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(
            tempFile,
            """{"timestamp":"2026-03-26T10:00:01Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"missing meta"}]}}""");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_Handles_unknown_roles_blank_content_missing_cmd_and_missing_exit_code()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-parser","timestamp":"2026-03-26T10:00:00Z","cwd":"C:\\repo"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"system","content":[{"type":"output_text","text":"system note"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"output_text","text":"developer note"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"mystery","content":[{"type":"output_text","text":"unknown note"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant"}}""",
                """{"type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":""}}""",
                """{"type":"response_item","payload":{"type":"function_call_output","name":"exec_command","output":"Command completed successfully."}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-parser", parsed.SessionId);
            Assert.Contains(parsed.Document.Events, item => item.Actor is SessionActor.System);
            Assert.Contains(parsed.Document.Events, item => item.Actor is SessionActor.Developer);
            Assert.Contains(parsed.Document.Events, item => item.Actor is SessionActor.Unknown);
            Assert.DoesNotContain(parsed.TechnicalBreadcrumbs.Commands, static command => !string.IsNullOrWhiteSpace(command));
            Assert.Empty(parsed.TechnicalBreadcrumbs.ExitCodes);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_CoversAdditionalRoleAndGuardBranches()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-roles","cwd":"C:\\repo","timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"dev note"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"system","content":[{"type":"output_text","text":"system note"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"note","content":[{"type":"output_text","text":"unknown role text"}]}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant"}}""",
                """{"type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":""}}""",
                """{"type":"response_item","payload":{"type":"function_call_output","name":"exec_command","output":"completed successfully"}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-roles", parsed.SessionId);
            Assert.Contains(parsed.Document.Events, item => item.Actor == SessionActor.Developer && item.Text == "dev note");
            Assert.Contains(parsed.Document.Events, item => item.Actor == SessionActor.System && item.Text == "system note");
            Assert.Contains(parsed.Document.Events, item => item.Actor == SessionActor.Unknown && item.Text == "unknown role text");
            Assert.DoesNotContain(parsed.TechnicalBreadcrumbs.Commands, static command => !string.IsNullOrWhiteSpace(command));
            Assert.Empty(parsed.TechnicalBreadcrumbs.ExitCodes);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RebuildAsync_IgnoresMalformedSessionIndexEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var liveRoot = Path.Combine(root, ".codex");
        var liveSessions = Path.Combine(liveRoot, "sessions");
        Directory.CreateDirectory(liveSessions);

        var sessionPath = Path.Combine(liveSessions, "session-idx.jsonl");
        await File.WriteAllLinesAsync(
            sessionPath,
            [
                """{"type":"session_meta","payload":{"id":"session-idx","cwd":"C:\\repo","timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}}"""
            ]);
        await File.WriteAllLinesAsync(
            Path.Combine(liveRoot, "session_index.jsonl"),
            [
                """{"thread_name":"missing id"}""",
                """{"id":"","thread_name":"blank id"}""",
                """{"id":"session-idx","thread_name":"Indexed thread"}"""
            ]);

        try
        {
            var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));
            await repository.InitializeAsync(CancellationToken.None);
            var indexer = new SessionWorkspaceIndexer(repository);

            var sessions = await indexer.RebuildAsync(
                [
                    new KnownSessionStore(
                        liveRoot,
                        SessionStoreKind.Live,
                        liveSessions,
                        Path.Combine(liveRoot, "session_index.jsonl"))
                ],
                CancellationToken.None);

            var indexed = Assert.Single(sessions);
            Assert.Equal("Indexed thread", indexed.ThreadName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsForMissingOrMismatchedTypedConfirmation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        Directory.CreateDirectory(sourceDir);
        var sessionPath = Path.Combine(sourceDir, "session-3.jsonl");
        await File.WriteAllTextAsync(sessionPath, "payload");

        var preview = new MaintenancePlanner().CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Archive,
                [new SessionPhysicalCopy("session-3", sessionPath, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 7, false))],
                "ARCHIVE 1 FILE"));
        var executor = new MaintenanceExecutor(Path.Combine(root, "checkpoints"));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(preview, Path.Combine(root, "archive"), string.Empty, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(preview, Path.Combine(root, "archive"), "WRONG", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReconcileMovesTargetsIntoReconciledFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        var destinationDir = Path.Combine(root, "destination");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceDir);

        var sessionPath = Path.Combine(sourceDir, "session-4.jsonl");
        await File.WriteAllTextAsync(sessionPath, "payload");

        var preview = new MaintenancePlanner().CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Reconcile,
                [new SessionPhysicalCopy("session-4", sessionPath, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 7, false))],
                "RECONCILE 1 FILE"));
        var executor = new MaintenanceExecutor(checkpointDir);

        try
        {
            var result = await executor.ExecuteAsync(preview, destinationDir, "RECONCILE 1 FILE", CancellationToken.None);
            var reconciledRoot = Path.Combine(destinationDir, "reconciled");
            Assert.True(result.Executed);
            Assert.Single(result.MovedTargets);
            Assert.StartsWith(
                Path.GetFullPath(reconciledRoot),
                Path.GetFullPath(Path.GetDirectoryName(result.MovedTargets[0].FilePath)!),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.ManifestPath));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath));
            Assert.Equal("Reconcile", manifest.RootElement.GetProperty("action").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
