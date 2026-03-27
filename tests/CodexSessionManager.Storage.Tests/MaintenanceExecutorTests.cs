using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

public sealed class MaintenanceExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ArchivesAllowedTargets_AndWritesCheckpointManifestAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        var archiveDir = Path.Combine(root, "archive");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceDir);

        var filePath = Path.Combine(sourceDir, "session-1.jsonl");
        await File.WriteAllTextAsync(filePath, "test");

        var executor = new MaintenanceExecutor(checkpointDir);
        var preview = MaintenancePlanner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Archive,
                [
                    new SessionPhysicalCopy("session-1", filePath, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 4, false))
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
    public async Task ExecuteAsync_DeleteMovesTargetsIntoCheckpointDeletedAreaAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions_backup");
        var destinationDir = Path.Combine(root, "ignored-destination");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceDir);

        var filePath = Path.Combine(sourceDir, "session-delete.jsonl");
        await File.WriteAllTextAsync(filePath, "delete me");

        var executor = new MaintenanceExecutor(checkpointDir);
        var preview = MaintenancePlanner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Delete,
                [
                    new SessionPhysicalCopy("session-delete", filePath, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 9, false))
                ],
                "DELETE 1 FILE"));

        try
        {
            var result = await executor.ExecuteAsync(preview, destinationDir, "DELETE 1 FILE", CancellationToken.None);

            Assert.True(result.Executed);
            Assert.False(File.Exists(filePath));
            var expectedDeletedRoot = Path.GetFullPath(Path.Combine(checkpointDir, "deleted") + Path.DirectorySeparatorChar);
            Assert.Contains(
                result.MovedTargets,
                moved => Path.GetFullPath(moved.FilePath).StartsWith(expectedDeletedRoot, StringComparison.OrdinalIgnoreCase));
            Assert.True(Directory.Exists(Path.Combine(checkpointDir, "deleted")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesUniqueDestinationNames_WhenBasenamesCollideAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceA = Path.Combine(root, "a");
        var sourceB = Path.Combine(root, "b");
        var archiveDir = Path.Combine(root, "archive");
        var checkpointDir = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);

        var fileA = Path.Combine(sourceA, "shared-name.jsonl");
        var fileB = Path.Combine(sourceB, "shared-name.jsonl");
        await File.WriteAllTextAsync(fileA, "a");
        await File.WriteAllTextAsync(fileB, "b");

        var executor = new MaintenanceExecutor(checkpointDir);
        var preview = MaintenancePlanner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Archive,
                [
                    new SessionPhysicalCopy("a", fileA, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false)),
                    new SessionPhysicalCopy("b", fileB, SessionStoreKind.Backup, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1, false))
                ],
                "ARCHIVE 2 FILES"));

        try
        {
            var result = await executor.ExecuteAsync(preview, archiveDir, "ARCHIVE 2 FILES", CancellationToken.None);

            Assert.Equal(2, result.MovedTargets.Count);
            Assert.Equal(2, result.MovedTargets.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

