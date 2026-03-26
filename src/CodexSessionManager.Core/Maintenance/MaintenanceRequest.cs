using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Core.Maintenance;

[ExcludeFromCodeCoverage]
public sealed record MaintenanceRequest
{
    public MaintenanceRequest(
        MaintenanceAction action,
        IReadOnlyList<SessionPhysicalCopy> targets,
        string typedConfirmation)
    {
        Action = action;
        Targets = targets;
        TypedConfirmation = typedConfirmation;
    }

    public MaintenanceAction Action { get; init; }

    public IReadOnlyList<SessionPhysicalCopy> Targets { get; init; }

    public string TypedConfirmation { get; init; }
}
