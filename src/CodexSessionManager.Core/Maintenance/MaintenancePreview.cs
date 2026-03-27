using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Maintenance;

[ExcludeFromCodeCoverage]
public sealed record MaintenancePreview
{
    public MaintenancePreview(
        MaintenanceAction Action,
        IReadOnlyList<SessionPhysicalCopy> AllowedTargets,
        IReadOnlyList<SessionPhysicalCopy> BlockedTargets,
        IReadOnlyList<MaintenanceWarning> Warnings,
        bool RequiresCheckpoint,
        bool RequiresTypedConfirmation,
        string RequiredTypedConfirmation)
    {
        this.Action = Action;
        this.AllowedTargets = AllowedTargets ?? throw new ArgumentNullException(nameof(AllowedTargets));
        this.BlockedTargets = BlockedTargets ?? throw new ArgumentNullException(nameof(BlockedTargets));
        this.Warnings = Warnings ?? throw new ArgumentNullException(nameof(Warnings));
        this.RequiresCheckpoint = RequiresCheckpoint;
        this.RequiresTypedConfirmation = RequiresTypedConfirmation;
        this.RequiredTypedConfirmation = RequiredTypedConfirmation ?? throw new ArgumentNullException(nameof(RequiredTypedConfirmation));
    }

    public MaintenanceAction Action { get; init; }

    public IReadOnlyList<SessionPhysicalCopy> AllowedTargets { get; init; }

    public IReadOnlyList<SessionPhysicalCopy> BlockedTargets { get; init; }

    public IReadOnlyList<MaintenanceWarning> Warnings { get; init; }

    public bool RequiresCheckpoint { get; init; }

    public bool RequiresTypedConfirmation { get; init; }

    public string RequiredTypedConfirmation { get; init; }

    public void Deconstruct(
        out MaintenanceAction Action,
        out IReadOnlyList<SessionPhysicalCopy> AllowedTargets,
        out IReadOnlyList<SessionPhysicalCopy> BlockedTargets,
        out IReadOnlyList<MaintenanceWarning> Warnings,
        out bool RequiresCheckpoint,
        out bool RequiresTypedConfirmation,
        out string RequiredTypedConfirmation)
    {
        Action = this.Action;
        AllowedTargets = this.AllowedTargets;
        BlockedTargets = this.BlockedTargets;
        Warnings = this.Warnings;
        RequiresCheckpoint = this.RequiresCheckpoint;
        RequiresTypedConfirmation = this.RequiresTypedConfirmation;
        RequiredTypedConfirmation = this.RequiredTypedConfirmation;
    }
}
