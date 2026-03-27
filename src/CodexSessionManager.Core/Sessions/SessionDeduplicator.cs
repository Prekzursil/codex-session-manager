// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

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

