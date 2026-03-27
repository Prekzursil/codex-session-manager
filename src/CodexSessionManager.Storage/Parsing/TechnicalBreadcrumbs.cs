// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
namespace CodexSessionManager.Storage.Parsing; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.

public sealed record TechnicalBreadcrumbs(
    IReadOnlyList<string> Commands,
    IReadOnlyList<int> ExitCodes,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> Urls);

