using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Maintenance;

public static class MaintenancePlanner
{
    private static readonly string[] ProtectedPathMarkers =
    [
        @"\.codex\sessions\",
        @"\.codex\state_5.sqlite",
        @"\.codex\codex-sqlite\"
    ];

    public static MaintenancePreview CreatePreview(MaintenanceRequest request)
    {
        var blockedTargets = new List<SessionPhysicalCopy>();
        var allowedTargets = new List<SessionPhysicalCopy>();
        var warnings = new List<MaintenanceWarning>();

        foreach (var candidate in request.Targets)
        {
            if (IsProtected(candidate))
            {
                blockedTargets.Add(candidate);
                warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Protected path blocked: {candidate.FilePath}"));
                continue;
            }

            allowedTargets.Add(candidate);
            warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Dangerous maintenance target: {candidate.FilePath}"));
        }

        return new MaintenancePreview(
            request.Action,
            AllowedTargets: allowedTargets,
            BlockedTargets: blockedTargets,
            Warnings: warnings,
            RequiresCheckpoint: true,
            RequiresTypedConfirmation: true,
            RequiredTypedConfirmation: request.TypedConfirmation);
    }

    private static bool IsProtected(SessionPhysicalCopy candidate)
    {
        var normalizedPath = candidate.FilePath.Replace('/', '\\');
        return candidate.StoreKind is SessionStoreKind.Live
            || ProtectedPathMarkers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
