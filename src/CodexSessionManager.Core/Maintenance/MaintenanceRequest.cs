// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Diagnostics.CodeAnalysis; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
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

