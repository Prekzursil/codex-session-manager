namespace CodexSessionManager.Core.Sessions;

public sealed record SessionSearchDocument(
    string ReadableTranscript,
    string DialogueTranscript,
    string ToolSummary,
    string CommandText,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> Urls,
    string ErrorText,
    string Alias,
    IReadOnlyList<string> Tags,
    string Notes)
{
    public string CombinedText =>
        string.Join(
            "\n",
            new[]
            {
                ReadableTranscript,
                DialogueTranscript,
                ToolSummary,
                CommandText,
                string.Join(' ', FilePaths),
                string.Join(' ', Urls),
                ErrorText,
                Alias,
                string.Join(' ', Tags),
                Notes
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));
}
