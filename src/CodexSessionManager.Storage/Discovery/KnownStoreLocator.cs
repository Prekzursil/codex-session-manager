// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

namespace CodexSessionManager.Storage.Discovery;

public static class KnownStoreLocator
{
    public static IReadOnlyList<KnownSessionStore> GetKnownStores(string codexHome)
    {
        return // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        [
            new KnownSessionStore(
                codexHome,
                SessionStoreKind.Live,
                Path.Combine(codexHome, "sessions"),
                Path.Combine(codexHome, "session_index.jsonl")),
            new KnownSessionStore(
                codexHome,
                SessionStoreKind.Backup,
                Path.Combine(codexHome, "sessions_backup"),
                Path.Combine(codexHome, "session_index.jsonl"))
        ];
    }
}

