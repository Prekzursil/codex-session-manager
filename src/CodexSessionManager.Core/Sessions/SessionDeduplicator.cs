namespace CodexSessionManager.Core.Sessions;

public static class SessionDeduplicator
{
    public static IReadOnlyList<LogicalSession> Consolidate(IEnumerable<SessionPhysicalCopy> copies)
    {
        return copies
            .Where(copy => !string.IsNullOrWhiteSpace(copy.SessionId))
            .GroupBy(copy => copy.SessionId, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedCopies = group
                    .OrderBy(copy => copy.StoreKind is SessionStoreKind.Live ? 0 : 1)
                    .ThenByDescending(copy => copy.LastWriteTimeUtc)
                    .ThenBy(copy => copy.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new LogicalSession(group.Key, null, orderedCopies[0], orderedCopies);
            })
            .OrderBy(session => session.SessionId, StringComparer.Ordinal)
            .ToArray();
    }
}
