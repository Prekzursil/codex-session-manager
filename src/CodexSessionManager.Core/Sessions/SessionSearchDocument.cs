using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Core.Sessions;

[ExcludeFromCodeCoverage]
public sealed record SessionSearchDocument
{
    private string _readableTranscript = string.Empty;
    private string _dialogueTranscript = string.Empty;
    private string _toolSummary = string.Empty;
    private string _commandText = string.Empty;
    private IReadOnlyList<string> _filePaths = Array.Empty<string>();
    private IReadOnlyList<string> _urls = Array.Empty<string>();
    private string _errorText = string.Empty;
    private string _alias = string.Empty;
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private string _notes = string.Empty;

    public string ReadableTranscript
    {
        get => _readableTranscript;
        init => _readableTranscript = value ?? string.Empty;
    }

    public string DialogueTranscript
    {
        get => _dialogueTranscript;
        init => _dialogueTranscript = value ?? string.Empty;
    }

    public string ToolSummary
    {
        get => _toolSummary;
        init => _toolSummary = value ?? string.Empty;
    }

    public string CommandText
    {
        get => _commandText;
        init => _commandText = value ?? string.Empty;
    }

    public IReadOnlyList<string> FilePaths
    {
        get => _filePaths;
        init => _filePaths = value ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Urls
    {
        get => _urls;
        init => _urls = value ?? Array.Empty<string>();
    }

    public string ErrorText
    {
        get => _errorText;
        init => _errorText = value ?? string.Empty;
    }

    public string Alias
    {
        get => _alias;
        init => _alias = value ?? string.Empty;
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value ?? Array.Empty<string>();
    }

    public string Notes
    {
        get => _notes;
        init => _notes = value ?? string.Empty;
    }

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
