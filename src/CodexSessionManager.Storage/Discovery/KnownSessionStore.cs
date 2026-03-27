using System.Diagnostics.CodeAnalysis;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Discovery;

[ExcludeFromCodeCoverage]
public sealed record KnownSessionStore
{
    public KnownSessionStore(
        string WorkspaceRoot,
        SessionStoreKind StoreKind,
        string SessionsPath,
        string SessionIndexPath)
    {
        this.WorkspaceRoot = WorkspaceRoot ?? throw new ArgumentNullException(nameof(WorkspaceRoot));
        this.StoreKind = StoreKind;
        this.SessionsPath = SessionsPath ?? throw new ArgumentNullException(nameof(SessionsPath));
        this.SessionIndexPath = SessionIndexPath ?? throw new ArgumentNullException(nameof(SessionIndexPath));
    }

    public string WorkspaceRoot { get; init; }

    public SessionStoreKind StoreKind { get; init; }

    public string SessionsPath { get; init; }

    public string SessionIndexPath { get; init; }
}
