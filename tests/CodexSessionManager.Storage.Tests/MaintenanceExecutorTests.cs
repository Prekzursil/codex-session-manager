using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

public sealed class MaintenanceExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ArchivesAllowedTargets_AndWritesCheckpointManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        var archiveDir = Path.Combine(root, "archive");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceDir);

        var filePath = Path.Combine(sourceDir, "session-1.jsonl");
        await File.WriteAllTextAsync(filePath, "test");

        var planner = new MaintenancePlanner();
        var executor = new MaintenanceExecutor(checkpointDir);
        var preview = planner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Archive,
                [
                    new SessionPhysicalCopy(
                        "session-1",
                        filePath,
                        SessionStoreKind.Backup,
                        DateTimeOffset.UtcNow,
                        4,
                        false)
                ],
                "ARCHIVE 1 FILE"));

        try
        {
            var result = await executor.ExecuteAsync(preview, archiveDir, "ARCHIVE 1 FILE", CancellationToken.None);

            Assert.True(result.Executed);
            Assert.Single(result.MovedTargets);
            Assert.False(File.Exists(filePath));
            Assert.True(File.Exists(Path.Combine(archiveDir, "session-1.jsonl")));

            var manifestPath = Assert.Single(Directory.GetFiles(checkpointDir, "*.json"));
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal("Archive", manifestDocument.RootElement.GetProperty("action").GetString());
            Assert.Equal(1, manifestDocument.RootElement.GetProperty("targets").GetArrayLength());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeleteMovesTargetsIntoCheckpointDeletedArea()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        var destinationDir = Path.Combine(root, "ignored-destination");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceDir);

        var filePath = Path.Combine(sourceDir, "session-delete.jsonl");
        await File.WriteAllTextAsync(filePath, "delete me");

        var planner = new MaintenancePlanner();
        var executor = new MaintenanceExecutor(checkpointDir);
        var preview = planner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Delete,
                [
                    new SessionPhysicalCopy(
                        "session-delete",
                        filePath,
                        SessionStoreKind.Backup,
                        DateTimeOffset.UtcNow,
                        9,
                        false)
                ],
                "DELETE 1 FILE"));

        try
        {
            var result = await executor.ExecuteAsync(preview, destinationDir, "DELETE 1 FILE", CancellationToken.None);

            Assert.True(result.Executed);
            Assert.False(File.Exists(filePath));
            Assert.Contains(result.MovedTargets, moved => moved.FilePath.Contains(@"\checkpoints\deleted\", StringComparison.OrdinalIgnoreCase));
            Assert.True(Directory.Exists(Path.Combine(checkpointDir, "deleted")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
