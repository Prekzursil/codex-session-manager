// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Diagnostics.CodeAnalysis; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

namespace CodexSessionManager.Core.Sessions;

public readonly record struct SessionPhysicalCopyState
{
    public SessionPhysicalCopyState(DateTimeOffset lastWriteTimeUtc, long fileSizeBytes, bool isHot)
    {
        LastWriteTimeUtc = lastWriteTimeUtc;
        FileSizeBytes = fileSizeBytes;
        IsHot = isHot;
    }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long FileSizeBytes { get; init; }

    public bool IsHot { get; init; }
}

[ExcludeFromCodeCoverage]
public sealed record SessionPhysicalCopy
{
    public SessionPhysicalCopy(
        string sessionId,
        string filePath,
        SessionStoreKind storeKind,
        SessionPhysicalCopyState state)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        StoreKind = storeKind;
        LastWriteTimeUtc = state.LastWriteTimeUtc;
        FileSizeBytes = state.FileSizeBytes;
        IsHot = state.IsHot;
    }

    public string SessionId { get; init; }

    public string FilePath { get; init; }

    public SessionStoreKind StoreKind { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long FileSizeBytes { get; init; }

    public bool IsHot { get; init; }
}

