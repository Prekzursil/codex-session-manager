#pragma warning disable S3990
namespace CodexSessionManager.Core.Sessions;

public enum SessionStoreKind
{
    Unknown = 0,
    Live = 1,
    Backup = 2,
    Mirror = 3,
    Other = 4
}

