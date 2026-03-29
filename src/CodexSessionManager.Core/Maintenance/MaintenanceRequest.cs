#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Maintenance;

[ExcludeFromCodeCoverage]
public sealed record MaintenanceRequest
{
    public MaintenanceRequest(
        MaintenanceAction Action,
        IReadOnlyList<SessionPhysicalCopy> Targets,
        string typedConfirmation)
    {
        this.Action = Action;
        this.Targets = Targets ?? throw new ArgumentNullException(nameof(Targets));
        TypedConfirmation = typedConfirmation ?? throw new ArgumentNullException(nameof(typedConfirmation));
    }

    public MaintenanceAction Action { get; init; }

    public IReadOnlyList<SessionPhysicalCopy> Targets { get; init; }

    public string TypedConfirmation { get; init; }
}

