// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Maintenance; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
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
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var action = request.Action;
        var requiredTypedConfirmation = request.TypedConfirmation;
        var blockedTargets = new List<SessionPhysicalCopy>();
        var allowedTargets = new List<SessionPhysicalCopy>();
        var warnings = new List<MaintenanceWarning>();

        var targets = request.Targets ?? [];
        foreach (var candidate in targets)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            var filePath = candidate.FilePath;
            if (IsProtected(candidate))
            {
                blockedTargets.Add(candidate);
                warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Protected path blocked: {filePath}"));
                continue;
            }

            allowedTargets.Add(candidate);
            warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Dangerous maintenance target: {filePath}"));
        }

        return new MaintenancePreview
        {
            Action = action,
            AllowedTargets = allowedTargets,
            BlockedTargets = blockedTargets,
            Warnings = warnings,
            RequiresCheckpoint = true,
            RequiresTypedConfirmation = true,
            RequiredTypedConfirmation = requiredTypedConfirmation
        };
    }

    private static bool IsProtected(SessionPhysicalCopy candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        var normalizedPath = candidate.FilePath.Replace('/', '\\');
        return candidate.StoreKind is SessionStoreKind.Live
            || ProtectedPathMarkers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

