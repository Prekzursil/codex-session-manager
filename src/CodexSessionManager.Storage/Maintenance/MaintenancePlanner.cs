#pragma warning disable S3990
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
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var action = request.Action; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var requiredTypedConfirmation = request.TypedConfirmation; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var blockedTargets = new List<SessionPhysicalCopy>();
        var allowedTargets = new List<SessionPhysicalCopy>();
        var warnings = new List<MaintenanceWarning>();

        var targets = request.Targets ?? []; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
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

        var normalizedPath = candidate.FilePath.Replace('/', '\\'); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        return candidate.StoreKind is SessionStoreKind.Live // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
            || ProtectedPathMarkers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

