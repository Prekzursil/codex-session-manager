using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionPhysicalCopy
{
    public SessionPhysicalCopy(
        string sessionId,
        string filePath,
        SessionStoreKind storeKind,
        DateTimeOffset lastWriteTimeUtc,
        long fileSizeBytes,
        bool isHot)
    {
        SessionId = sessionId;
        FilePath = filePath;
        StoreKind = storeKind;
        LastWriteTimeUtc = lastWriteTimeUtc;
        FileSizeBytes = fileSizeBytes;
        IsHot = isHot;
    }

    public string SessionId { get; init; }

    public string FilePath { get; init; }

    public SessionStoreKind StoreKind { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long FileSizeBytes { get; init; }

    public bool IsHot { get; init; }
}
