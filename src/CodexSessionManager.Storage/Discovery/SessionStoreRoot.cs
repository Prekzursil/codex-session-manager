// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using CodexSessionManager.Core.Sessions; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

namespace CodexSessionManager.Storage.Discovery;

public sealed record SessionStoreRoot(
    string RootPath,
    SessionStoreKind StoreKind);

