#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
namespace CodexSessionManager.Storage.Parsing;

public sealed record TechnicalBreadcrumbs(
    IReadOnlyList<string> Commands,
    IReadOnlyList<int> ExitCodes,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> Urls);

