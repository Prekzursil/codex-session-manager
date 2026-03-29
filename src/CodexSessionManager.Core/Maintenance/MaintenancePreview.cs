#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Maintenance;

[ExcludeFromCodeCoverage]
public sealed record MaintenancePreview
{
    private IReadOnlyList<SessionPhysicalCopy> _allowedTargets = [];
    private IReadOnlyList<SessionPhysicalCopy> _blockedTargets = [];
    private IReadOnlyList<MaintenanceWarning> _warnings = [];
    private string _requiredTypedConfirmation = string.Empty;

    public MaintenanceAction Action { get; init; }

    public required IReadOnlyList<SessionPhysicalCopy> AllowedTargets
    {
        get => _allowedTargets;
        init => _allowedTargets = value ?? throw new ArgumentNullException(nameof(AllowedTargets));
    }

    public required IReadOnlyList<SessionPhysicalCopy> BlockedTargets
    {
        get => _blockedTargets;
        init => _blockedTargets = value ?? throw new ArgumentNullException(nameof(BlockedTargets));
    }

    public required IReadOnlyList<MaintenanceWarning> Warnings
    {
        get => _warnings;
        init => _warnings = value ?? throw new ArgumentNullException(nameof(Warnings));
    }

    public bool RequiresCheckpoint { get; init; }

    public bool RequiresTypedConfirmation { get; init; }

    public required string RequiredTypedConfirmation
    {
        get => _requiredTypedConfirmation;
        init => _requiredTypedConfirmation = value ?? throw new ArgumentNullException(nameof(RequiredTypedConfirmation));
    }
}

