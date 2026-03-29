using System.Reflection;
using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

[Collection("CurrentDirectorySensitive")]
public sealed partial class StorageCoverageExpansionTests
{
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

    [Fact]
    public async Task MaintenanceExecutor_rejects_blank_constructor_and_null_inputsAsync()
    {
        Assert.Throws<ArgumentException>(() => new MaintenanceExecutor(" "));

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sourceDir);
        var sessionPath = Path.Combine(sourceDir, "session-maintenance.jsonl");
        await File.WriteAllTextAsync(sessionPath, "payload");

        var preview = MaintenancePlanner.CreatePreview(
            new MaintenanceRequest(
                MaintenanceAction.Archive,
                [new SessionPhysicalCopy("session-maintenance", sessionPath, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 5, false))],
                "ARCHIVE 1 FILE"));
        var executor = new MaintenanceExecutor(Path.Combine(root, "checkpoints"));

        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(null!, Path.Combine(root, "archive"), "ARCHIVE 1 FILE", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(preview, null!, "ARCHIVE 1 FILE", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(preview, Path.Combine(root, "archive"), null!, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionDiscoveryService_preserves_filesystem_root_paths()
    {
        var normalizeRootPathMethod = typeof(SessionDiscoveryService).GetMethod("NormalizeRootPath", BindingFlags.NonPublic | BindingFlags.Static)!;
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;

        var normalizedRoot = (string)normalizeRootPathMethod.Invoke(null, [filesystemRoot])!;

        Assert.Equal(filesystemRoot, normalizedRoot);
    }

    [Fact]
    public async Task SessionCatalogRepository_preserves_existing_metadata_when_new_session_is_blankAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sessionFile = Path.Combine(root, "session-preserve.jsonl");
        await File.WriteAllTextAsync(sessionFile, "payload");
        var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));

        try
        {
            await repository.InitializeAsync(CancellationToken.None);

            var existingSession = new IndexedLogicalSession(
                "session-preserve",
                "Preserve Thread",
                new SessionPhysicalCopy("session-preserve", sessionFile, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 9, false)),
                [new SessionPhysicalCopy("session-preserve", sessionFile, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 9, false))],
                new SessionSearchDocument
                {
                    ReadableTranscript = "readable",
                    DialogueTranscript = "dialogue",
                    ToolSummary = "tool summary",
                    CommandText = "codex resume",
                    FilePaths = [sessionFile],
                    Urls = ["https://example.com"],
                    ErrorText = string.Empty,
                    Alias = "saved alias",
                    Tags = ["one", "two"],
                    Notes = "saved notes",
                });
            await repository.UpsertAsync(existingSession, CancellationToken.None);

            var blankMetadataSession = existingSession with
            {
                SearchDocument = new SessionSearchDocument
                {
                    ReadableTranscript = "readable",
                    DialogueTranscript = "dialogue",
                    ToolSummary = "tool summary",
                    CommandText = "codex resume",
                    FilePaths = [sessionFile],
                    Urls = ["https://example.com"],
                    ErrorText = string.Empty,
                    Alias = string.Empty,
                    Tags = [],
                    Notes = string.Empty,
                },
            };
            await repository.UpsertAsync(blankMetadataSession, CancellationToken.None);

            var storedSession = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));
            Assert.Equal("saved alias", storedSession.SearchDocument.Alias);
            Assert.Equal(new[] { "one", "two" }, storedSession.SearchDocument.Tags);
            Assert.Equal("saved notes", storedSession.SearchDocument.Notes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
