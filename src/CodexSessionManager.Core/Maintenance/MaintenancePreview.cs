using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Maintenance;

[ExcludeFromCodeCoverage]
public sealed record MaintenancePreview(
    MaintenanceAction Action,
    IReadOnlyList<SessionPhysicalCopy> AllowedTargets,
    IReadOnlyList<SessionPhysicalCopy> BlockedTargets,
    IReadOnlyList<MaintenanceWarning> Warnings,
    bool RequiresCheckpoint,
    bool RequiresTypedConfirmation,
    string RequiredTypedConfirmation);
