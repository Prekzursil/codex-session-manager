using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Maintenance;

namespace CodexSessionManager.Storage.Tests;

public sealed class MaintenancePlannerTests
{
    [Fact]
    public void CreateDeletePreview_RejectsProtectedPaths_AndRequiresTypedConfirmationAndCheckpoint()
    {
        var protectedCopy = new SessionPhysicalCopy("session-1", @"C:\Users\Prekzursil\.codex\sessions\2026\03\23\session-1.jsonl", SessionStoreKind.Live, new SessionPhysicalCopyState(new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero), 1000, false));

        var backupCopy = new SessionPhysicalCopy("session-1", @"C:\Users\Prekzursil\.codex\sessions_backup\2026\03\23\session-1.jsonl", SessionStoreKind.Backup, new SessionPhysicalCopyState(new DateTimeOffset(2026, 3, 23, 9, 0, 0, TimeSpan.Zero), 1000, false));

        var request = new MaintenanceRequest(
            MaintenanceAction.Delete,
            [protectedCopy, backupCopy],
            typedConfirmation: "DELETE 2 FILES");

        var preview = MaintenancePlanner.CreatePreview(request);

        Assert.True(preview.RequiresCheckpoint);
        Assert.True(preview.RequiresTypedConfirmation);
        Assert.Contains(preview.BlockedTargets, blocked => blocked.FilePath == protectedCopy.FilePath);
        Assert.Contains(preview.AllowedTargets, allowed => allowed.FilePath == backupCopy.FilePath);
        Assert.Contains(preview.Warnings, warning => warning.Severity is MaintenanceWarningSeverity.Dangerous);
    }
}

