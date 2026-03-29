#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record IndexedLogicalSession
{
    public IndexedLogicalSession(
        string SessionId,
        string ThreadName,
        SessionPhysicalCopy PreferredCopy,
        IReadOnlyList<SessionPhysicalCopy> PhysicalCopies,
        SessionSearchDocument SearchDocument)
    {
        this.SessionId = SessionId ?? throw new ArgumentNullException(nameof(SessionId));
        this.ThreadName = ThreadName ?? string.Empty;
        this.PreferredCopy = PreferredCopy ?? throw new ArgumentNullException(nameof(PreferredCopy));
        this.PhysicalCopies = PhysicalCopies ?? throw new ArgumentNullException(nameof(PhysicalCopies));
        this.SearchDocument = SearchDocument ?? throw new ArgumentNullException(nameof(SearchDocument));
    }

    public string SessionId { get; init; }

    public string ThreadName { get; init; }

    public SessionPhysicalCopy PreferredCopy { get; init; }

    public IReadOnlyList<SessionPhysicalCopy> PhysicalCopies { get; init; }

    public SessionSearchDocument SearchDocument { get; init; }

    public void Deconstruct(
        out string SessionId,
        out string ThreadName,
        out SessionPhysicalCopy PreferredCopy,
        out IReadOnlyList<SessionPhysicalCopy> PhysicalCopies,
        out SessionSearchDocument SearchDocument)
    {
        SessionId = this.SessionId;
        ThreadName = this.ThreadName;
        PreferredCopy = this.PreferredCopy;
        PhysicalCopies = this.PhysicalCopies;
        SearchDocument = this.SearchDocument;
    }
}

