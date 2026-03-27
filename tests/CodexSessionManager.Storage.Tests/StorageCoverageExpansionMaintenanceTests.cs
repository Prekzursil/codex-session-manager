using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

public sealed partial class StorageCoverageExpansionTests
{
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
}
