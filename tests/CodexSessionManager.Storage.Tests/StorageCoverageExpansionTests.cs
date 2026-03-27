using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.Storage.Tests;

[Collection("CurrentDirectorySensitive")]
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
        await WriteAssistantSessionAsync(
            Path.Combine(customStore, "2026", "03", "26"),
            "session-1",
            "see https://example.com and C:\\repo\\file.txt");

        try
        {
            var catalog = await SessionDiscoveryService.DiscoverAsync(
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
    public async Task DiscoverAsync_Normalizes_sessions_backup_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(root, "sessions_backup");
        await WriteAssistantSessionAsync(
            Path.Combine(backupRoot, "2026", "03", "26"),
            "session-backup",
            "backup session");

        try
        {
            var catalog = await SessionDiscoveryService.DiscoverAsync(
                [new SessionStoreRoot(backupRoot.Replace('\\', '/'), SessionStoreKind.Backup)],
                CancellationToken.None);

            var logical = Assert.Single(catalog.LogicalSessions);
            Assert.Equal("session-backup", logical.SessionId);
            Assert.Equal(SessionStoreKind.Backup, logical.PreferredCopy.StoreKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_Normalizes_relative_sessions_backup_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var previousCurrentDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(root);
        Environment.CurrentDirectory = root;
        const string backupRoot = "sessions_backup";
        await WriteAssistantSessionAsync(
            Path.Combine(root, backupRoot, "2026", "03", "26"),
            "session-relative-backup",
            "relative backup session",
            "session-relative-backup.jsonl");

        try
        {
            var catalog = await SessionDiscoveryService.DiscoverAsync(
                [new SessionStoreRoot(backupRoot, SessionStoreKind.Backup)],
                CancellationToken.None);

            var logical = Assert.Single(catalog.LogicalSessions);
            Assert.Equal("session-relative-backup", logical.SessionId);
            Assert.Equal(SessionStoreKind.Backup, logical.PreferredCopy.StoreKind);
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
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
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
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
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
            Assert.Empty(parsed.TechnicalBreadcrumbs.ExitCodes);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_Ignores_unknown_types_and_missing_tool_metadata()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-extra","cwd":"C:\\repo","timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"unknown","payload":{"ignored":true}}""",
                """{"type":"response_item","payload":{"type":"unknown_payload","role":"assistant"}}""",
                """{"type":"response_item","payload":{"type":"function_call","arguments":"{}"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"image","text":"ignored"}]}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-extra", parsed.SessionId);
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
            Assert.Contains(parsed.Document.Events, item => item.Kind == NormalizedEventKind.ToolCall && item.ToolName == "unknown_tool");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_Ignores_non_string_json_properties_when_extracting_strings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-non-string","cwd":{"unexpected":true},"forked_from_id":null,"timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":{"cmd":"pwsh"}}}""",
                """{"type":"response_item","payload":{"type":"function_call_output","name":"exec_command","output":7}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":null}]}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-non-string", parsed.SessionId);
            Assert.Null(parsed.Cwd);
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
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
                """{"id":"session-idx-non-string","thread_name":17}""",
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
    public async Task DiscoverAsync_UsesSessionsBackupRoot_AndFallsBackWhenThreadNameIsNotString()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(root, "sessions_backup");
        Directory.CreateDirectory(backupRoot);

        var sessionPath = Path.Combine(backupRoot, "session-backup.jsonl");
        await File.WriteAllLinesAsync(
            sessionPath,
            [
                """{"type":"session_meta","payload":{"id":"session-backup","cwd":"C:\\repo","timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"backup ok"}]}}"""
            ]);
        await File.WriteAllLinesAsync(
            Path.Combine(root, "session_index.jsonl"),
            [
                """{"id":"session-backup","thread_name":7}"""
            ]);

        try
        {
            var catalog = await SessionDiscoveryService.DiscoverAsync(
                [
                    new SessionStoreRoot(backupRoot, SessionStoreKind.Backup)
                ],
                CancellationToken.None);

            var indexed = Assert.Single(catalog.LogicalSessions);
            Assert.Equal("session-backup", indexed.SessionId);
            Assert.Equal("session-backup", indexed.ThreadName);
            Assert.Equal(SessionStoreKind.Backup, indexed.PreferredCopy.StoreKind);
            Assert.Equal(sessionPath, indexed.PreferredCopy.FilePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseAsync_HandlesMissingToolNames_AndNonTextPayloadBranches()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-mixed","timestamp":"2026-03-26T10:00:00Z"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"input_text","text":"see https://example.com and C:\\repo\\file.txt"},{"type":"output_text","text":"   "},{"type":"image","text":"ignored"}]}}""",
                """{"type":"response_item","payload":{"type":"function_call","arguments":"{}"}}""",
                """{"type":"response_item","payload":{"type":"function_call_output","output":null}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-mixed", parsed.SessionId);
            Assert.Contains(parsed.Document.Events, item => item.Kind == NormalizedEventKind.ToolCall && item.ToolName == "unknown_tool");
            Assert.Contains(parsed.Document.Events, item => item.Kind == NormalizedEventKind.ToolOutput && item.ToolName == "tool");
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
            Assert.Empty(parsed.TechnicalBreadcrumbs.ExitCodes);
            Assert.Contains(@"C:\repo\file.txt", parsed.TechnicalBreadcrumbs.FilePaths);
            Assert.Contains("https://example.com", parsed.TechnicalBreadcrumbs.Urls);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_Ignores_blank_text_invalid_timestamp_and_missing_cmd_property()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(
            tempFile,
            [
                """{"type":"session_meta","payload":{"id":"session-blank-text","cwd":"C:\\repo","timestamp":"not-a-timestamp"}}""",
                """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"   "},{"type":"output_text","text":"kept text"}]}}""",
                """{"type":"response_item","payload":{"type":"function_call","name":"exec_command","arguments":"{\"other\":\"value\"}"}}""",
                """{"type":"response_item","payload":{"type":"function_call_output","name":"exec_command","output":"Process exited with code "}}"""
            ]);

        try
        {
            var parsed = await SessionJsonlParser.ParseAsync(tempFile, CancellationToken.None);

            Assert.Equal("session-blank-text", parsed.SessionId);
            Assert.Contains(parsed.Document.Events, item => item.Kind == NormalizedEventKind.Message && item.Text == "kept text");
            Assert.Contains(parsed.Document.Events, item => item.Kind == NormalizedEventKind.ToolCall && item.ToolName == "exec_command");
            Assert.Empty(parsed.TechnicalBreadcrumbs.Commands);
            Assert.Empty(parsed.TechnicalBreadcrumbs.ExitCodes);
        }
        finally
        {
            File.Delete(tempFile);
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

        var preview = MaintenancePlanner.CreatePreview(
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
    public async Task ExecuteAsync_ThrowsWhenPreviewIsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executor = new MaintenanceExecutor(Path.Combine(root, "checkpoints"));

        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                executor.ExecuteAsync(null!, Path.Combine(root, "archive"), "ARCHIVE 1 FILE", CancellationToken.None));
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

        var preview = MaintenancePlanner.CreatePreview(
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

    private static async Task WriteAssistantSessionAsync(string sessionDirectory, string sessionId, string assistantText, string? fileName = null)
    {
        Directory.CreateDirectory(sessionDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(sessionDirectory, fileName ?? $"{sessionId}.jsonl"),
            [
                $"{{\"timestamp\":\"2026-03-26T10:00:00Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"{sessionId}\",\"timestamp\":\"2026-03-26T10:00:00Z\",\"cwd\":\"C:\\\\repo\"}}}}",
                $"{{\"timestamp\":\"2026-03-26T10:00:01Z\",\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{{\"type\":\"output_text\",\"text\":\"{assistantText}\"}}]}}}}"
            ]);
    }
}

