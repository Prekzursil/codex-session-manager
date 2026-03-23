namespace CodexSessionManager.Storage.Parsing;

public sealed record TechnicalBreadcrumbs(
    IReadOnlyList<string> Commands,
    IReadOnlyList<int> ExitCodes,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> Urls);
