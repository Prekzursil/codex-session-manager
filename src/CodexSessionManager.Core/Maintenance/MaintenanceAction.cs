#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Core.Maintenance;

public enum MaintenanceAction
{
    Delete = 0,
    Archive = 1,
    Move = 2,
    Reconcile = 3
}

