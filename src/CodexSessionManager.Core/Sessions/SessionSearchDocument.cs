using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionSearchDocument
{
    public SessionSearchDocument(
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
        this.ReadableTranscript = ReadableTranscript ?? string.Empty;
        this.DialogueTranscript = DialogueTranscript ?? string.Empty;
        this.ToolSummary = ToolSummary ?? string.Empty;
        this.CommandText = CommandText ?? string.Empty;
        this.FilePaths = FilePaths ?? Array.Empty<string>();
        this.Urls = Urls ?? Array.Empty<string>();
        this.ErrorText = ErrorText ?? string.Empty;
        this.Alias = Alias ?? string.Empty;
        this.Tags = Tags ?? Array.Empty<string>();
        this.Notes = Notes ?? string.Empty;
    }

    public string ReadableTranscript { get; init; }

    public string DialogueTranscript { get; init; }

    public string ToolSummary { get; init; }

    public string CommandText { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; }

    public IReadOnlyList<string> Urls { get; init; }

    public string ErrorText { get; init; }

    public string Alias { get; init; }

    public IReadOnlyList<string> Tags { get; init; }

    public string Notes { get; init; }

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
