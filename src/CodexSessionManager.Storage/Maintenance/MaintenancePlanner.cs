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
        var previewRequest = CreatePreviewRequest(request);
        var blockedTargets = new List<SessionPhysicalCopy>();
        var allowedTargets = new List<SessionPhysicalCopy>();
        var warnings = new List<MaintenanceWarning>();

        foreach (var candidate in previewRequest.Targets)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            var candidatePath = candidate.FilePath;
            if (IsProtected(candidate))
            {
                blockedTargets.Add(candidate);
                warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Protected path blocked: {candidatePath}"));
                continue;
            }

            allowedTargets.Add(candidate);
            warnings.Add(new MaintenanceWarning(MaintenanceWarningSeverity.Dangerous, $"Dangerous maintenance target: {candidatePath}"));
        }

        return new MaintenancePreview
        {
            Action = previewRequest.Action,
            AllowedTargets = allowedTargets,
            BlockedTargets = blockedTargets,
            Warnings = warnings,
            RequiresCheckpoint = true,
            RequiresTypedConfirmation = true,
            RequiredTypedConfirmation = previewRequest.RequiredTypedConfirmation
        };
    }

    private static bool IsProtected(SessionPhysicalCopy candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var normalizedPath = NormalizePath(candidate.FilePath);
        return candidate.StoreKind is SessionStoreKind.Live
            || ProtectedPathMarkers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static PreviewRequest CreatePreviewRequest(MaintenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PreviewRequest(
            request.Action,
            request.TypedConfirmation,
            request.Targets ?? []);
    }

    private static string NormalizePath(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return filePath.Replace('/', '\\');
    }

    private readonly record struct PreviewRequest(
        MaintenanceAction Action,
        string RequiredTypedConfirmation,
        IReadOnlyList<SessionPhysicalCopy> Targets);
}

