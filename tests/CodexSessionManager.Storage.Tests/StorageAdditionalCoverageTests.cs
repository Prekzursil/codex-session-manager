using System.Reflection;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

public sealed partial class StorageCoverageExpansionTests
{
    private static readonly MethodInfo BuildDestinationPathMethod =
        typeof(MaintenanceExecutor).GetMethod("BuildDestinationPath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateKnownSessionStoreCoverageMethod =
        typeof(SessionDiscoveryService).GetMethod("CreateKnownSessionStore", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MoveTargetsCoverageMethod =
        typeof(MaintenanceExecutor).GetMethod("MoveTargets", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo NormalizeRootPathCoverageMethod =
        typeof(SessionDiscoveryService).GetMethod("NormalizeRootPath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WriteManifestAsyncMethod =
        typeof(MaintenanceExecutor).GetMethod("WriteManifestAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public async Task ExecuteAsync_rejects_previews_that_skip_typed_confirmationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executor = new MaintenanceExecutor(Path.Combine(root, "checkpoints"));
        var preview = new MaintenancePreview
        {
            Action = MaintenanceAction.Archive,
            AllowedTargets = [],
            BlockedTargets = [],
            Warnings = [],
            RequiresCheckpoint = false,
            RequiresTypedConfirmation = false,
            RequiredTypedConfirmation = string.Empty,
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                executor.ExecuteAsync(preview, Path.Combine(root, "archive"), "IGNORED", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MaintenanceExecutor_private_guards_and_hot_copy_paths_are_coveredAsync()
    {
        var nullTargetsException = Assert.Throws<TargetInvocationException>(() =>
            MoveTargetsCoverageMethod.Invoke(null, [null!, Path.GetTempPath(), CancellationToken.None]));
        Assert.IsType<ArgumentNullException>(nullTargetsException.InnerException);

        var blankDestinationException = Assert.Throws<TargetInvocationException>(() =>
            MoveTargetsCoverageMethod.Invoke(null, [Array.Empty<SessionPhysicalCopy>(), " ", CancellationToken.None]));
        Assert.IsType<ArgumentException>(blankDestinationException.InnerException);

        var blankRootException = Assert.Throws<TargetInvocationException>(() =>
            BuildDestinationPathMethod.Invoke(null, [" ", "session.jsonl"]));
        Assert.IsType<ArgumentException>(blankRootException.InnerException);

        var blankFileException = Assert.Throws<TargetInvocationException>(() =>
            BuildDestinationPathMethod.Invoke(null, [Path.GetTempPath(), " "]));
        Assert.IsType<ArgumentException>(blankFileException.InnerException);

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executor = new MaintenanceExecutor(Path.Combine(root, "checkpoints"));
        var manifestException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            (Task<string>)WriteManifestAsyncMethod.Invoke(executor, [MaintenanceAction.Archive, null!, CancellationToken.None])!);
        Assert.Equal("movedTargets", manifestException.ParamName);

        var sessionFile = Path.Combine(root, "session-hot.jsonl");
        await File.WriteAllTextAsync(sessionFile, "payload");
        var uniqueDestinationPath = (string)BuildDestinationPathMethod.Invoke(null, [root, Path.GetFileName(sessionFile)])!;
        Assert.NotEqual(Path.Combine(root, Path.GetFileName(sessionFile)), uniqueDestinationPath);
        Assert.StartsWith(Path.Combine(root, "session-hot-"), uniqueDestinationPath, StringComparison.Ordinal);
        Assert.EndsWith(".jsonl", uniqueDestinationPath, StringComparison.Ordinal);
        var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));

        try
        {
            await repository.InitializeAsync(CancellationToken.None);

            var hotCopy = new SessionPhysicalCopy(
                "session-hot",
                sessionFile,
                SessionStoreKind.Live,
                new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 42, true));
            var session = new IndexedLogicalSession(
                "session-hot",
                "Hot Thread",
                hotCopy,
                [hotCopy],
                new SessionSearchDocument
                {
                    ReadableTranscript = "readable",
                    DialogueTranscript = "dialogue",
                    ToolSummary = string.Empty,
                    CommandText = string.Empty,
                    FilePaths = [],
                    Urls = [],
                    ErrorText = string.Empty,
                    Alias = string.Empty,
                    Tags = [],
                    Notes = string.Empty,
                });

            await repository.UpsertAsync(session, CancellationToken.None);

            var stored = Assert.Single(await repository.ListSessionsAsync(CancellationToken.None));
            Assert.True(stored.PreferredCopy.IsHot);
            Assert.True(stored.PhysicalCopies.Single().IsHot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionDiscoveryService_private_branches_cover_additional_layouts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var backupRoot = Path.Combine(root, "backup-root");
            var backupStore = (KnownSessionStore)CreateKnownSessionStoreCoverageMethod.Invoke(
                null,
                [new SessionStoreRoot(backupRoot, SessionStoreKind.Backup)])!;
            Assert.Equal(backupRoot, backupStore.WorkspaceRoot);
            Assert.Equal(backupRoot, backupStore.SessionsPath);

            var sessionsBackupRoot = Path.Combine(root, "workspace", "sessions_backup");
            var normalizedBackupStore = (KnownSessionStore)CreateKnownSessionStoreCoverageMethod.Invoke(
                null,
                [new SessionStoreRoot(sessionsBackupRoot, SessionStoreKind.Backup)])!;
            Assert.Equal(Path.Combine(root, "workspace"), normalizedBackupStore.WorkspaceRoot);
            Assert.Equal(sessionsBackupRoot, normalizedBackupStore.SessionsPath);

            var mirrorRoot = Path.Combine(root, "mirror-root");
            var mirrorStore = (KnownSessionStore)CreateKnownSessionStoreCoverageMethod.Invoke(
                null,
                [new SessionStoreRoot(mirrorRoot, SessionStoreKind.Mirror)])!;
            Assert.Equal(mirrorRoot, mirrorStore.WorkspaceRoot);
            Assert.Equal(mirrorRoot, mirrorStore.SessionsPath);

            var nestedRoot = Path.Combine(root, "nested") + Path.DirectorySeparatorChar;
            var normalizedNestedRoot = (string)NormalizeRootPathCoverageMethod.Invoke(null, [nestedRoot])!;
            Assert.Equal(Path.Combine(root, "nested"), normalizedNestedRoot);

            var filesystemSeparatorRoot = Path.DirectorySeparatorChar.ToString();
            var normalizedSeparatorRoot = (string)NormalizeRootPathCoverageMethod.Invoke(null, [filesystemSeparatorRoot])!;
            Assert.Equal(filesystemSeparatorRoot, normalizedSeparatorRoot);

            if (OperatingSystem.IsWindows())
            {
                var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;
                var trimmedFilesystemRoot = filesystemRoot.TrimEnd(Path.DirectorySeparatorChar);
                if (!string.Equals(trimmedFilesystemRoot, filesystemRoot, StringComparison.Ordinal))
                {
                    var normalizedDriveRoot = (string)NormalizeRootPathCoverageMethod.Invoke(null, [trimmedFilesystemRoot])!;
                    Assert.Equal(Path.GetPathRoot(trimmedFilesystemRoot), normalizedDriveRoot);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
