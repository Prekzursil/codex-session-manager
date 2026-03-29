#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.Storage.Tests;

public sealed partial class StorageCoverageExpansionTests
{
    [Fact]
    public async Task ParseAsync_Ignores_blank_text_invalid_timestamp_and_missing_cmd_propertyAsync()
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
    public async Task ExecuteAsync_ThrowsForMissingOrMismatchedTypedConfirmationAsync()
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
    public async Task ExecuteAsync_ReconcileMovesTargetsIntoReconciledFolderAsync()
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
}
