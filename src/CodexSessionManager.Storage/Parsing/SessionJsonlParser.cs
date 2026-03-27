using System.Text.Json;
using System.Text.RegularExpressions;
using CodexSessionManager.Core.Transcripts;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Storage.Parsing;

[SuppressMessage("Code Smell", "S2333", Justification = "GeneratedRegex members require the containing type to be partial.")]
public static partial class SessionJsonlParser
{
    private static readonly Regex UrlRegex = UrlRegexFactory();
    private static readonly Regex FilePathRegex = FilePathRegexFactory();

    public static async Task<ParsedSessionFile> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var state = new ParseState();

        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            ParseLine(document.RootElement, state);
        }

        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            throw new InvalidOperationException($"Session ID was not found in {filePath}.");
        }

        return new ParsedSessionFile(
            SessionId: state.SessionId,
            ForkedFromId: state.ForkedFromId,
            Cwd: state.Cwd,
            TechnicalBreadcrumbs: new TechnicalBreadcrumbs(
                state.Commands,
                state.ExitCodes,
                state.FilePaths.ToArray(),
                state.Urls.ToArray()),
            Document: new NormalizedSessionDocument(
                state.SessionId,
                ThreadName: null,
                StartedAtUtc: state.StartedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : state.StartedAtUtc,
                ForkedFromId: state.ForkedFromId,
                Cwd: state.Cwd,
                Events: state.Events));
    }

    private static void ParseLine(JsonElement root, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var type = TryGetString(root, "type");
        if (type is "session_meta" && root.TryGetProperty("payload", out var sessionMetaPayload))
        {
            ParseSessionMetadata(sessionMetaPayload, state);
            return;
        }

        if (type is "response_item" && root.TryGetProperty("payload", out var responseItemPayload))
        {
            ParseResponseItem(responseItemPayload, state);
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var sessionId = TryGetString(payload, "id");
        if (state.SessionId is null)
        {
            state.SessionId = sessionId;
        }

        var forkedFromId = TryGetString(payload, "forked_from_id");
        if (state.ForkedFromId is null)
        {
            state.ForkedFromId = forkedFromId;
        }

        var currentWorkingDirectory = TryGetString(payload, "cwd");
        if (state.Cwd is null)
        {
            state.Cwd = currentWorkingDirectory;
        }

        if (state.StartedAtUtc != DateTimeOffset.MinValue)
        {
            return;
        }

        if (!payload.TryGetProperty("timestamp", out var timestampElement))
        {
            return;
        }

        var timestampValue = timestampElement.GetString() ?? string.Empty;
        if (DateTimeOffset.TryParse(timestampValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStartedAt))
        {
            state.StartedAtUtc = parsedStartedAt;
        }
    }

    private static void ParseResponseItem(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var payloadType = TryGetString(payload, "type");
        if (payloadType is "message")
        {
            ParseMessage(payload, state);
            return;
        }

        if (payloadType is "function_call")
        {
            ParseFunctionCall(payload, state);
            return;
        }

        if (payloadType is "function_call_output")
        {
            ParseFunctionCallOutput(payload, state);
        }
    }

    private static void ParseMessage(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (!payload.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        var role = TryGetString(payload, "role");
        var actor = ResolveActor(role);
        foreach (var contentItem in contentElement.EnumerateArray())
        {
            if (!IsTextContentItem(contentItem))
            {
                continue;
            }

            var text = contentItem.GetProperty("text").GetString()!;
            var messageEvent = NormalizedSessionEvent.CreateMessage(actor, text);
            state.Events.Add(messageEvent);
            ExtractFilePathsAndUrls(text, state.FilePaths, state.Urls);
        }
    }

    private static void ParseFunctionCall(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var toolName = TryGetString(payload, "name") ?? "unknown_tool";
        var rawArguments = TryGetString(payload, "arguments") ?? string.Empty;
        var toolCallEvent = NormalizedSessionEvent.CreateToolCall(toolName, rawArguments);
        state.Events.Add(toolCallEvent);

        var extractedCommand = TryExtractCommand(rawArguments);
        if (!string.IsNullOrWhiteSpace(extractedCommand))
        {
            state.Commands.Add(extractedCommand);
        }

        ExtractFilePathsAndUrls(rawArguments, state.FilePaths, state.Urls);
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var outputText = TryGetString(payload, "output") ?? string.Empty;
        var toolName = TryGetString(payload, "name") ?? "tool";
        var toolOutputEvent = NormalizedSessionEvent.CreateToolOutput(toolName, outputText);
        state.Events.Add(toolOutputEvent);

        if (TryExtractExitCode(outputText, out var exitCode))
        {
            var recordedExitCode = exitCode;
            state.ExitCodes.Add(recordedExitCode);
        }

        ExtractFilePathsAndUrls(outputText, state.FilePaths, state.Urls);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        var hasProperty = element.TryGetProperty(propertyName, out var propertyElement);
        if (!hasProperty)
        {
            return null;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.String => propertyElement.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static SessionActor ResolveActor(string? role)
    {
        return role switch
        {
            "user" => SessionActor.User,
            "assistant" => SessionActor.Assistant,
            "developer" => SessionActor.Developer,
            "system" => SessionActor.System,
            _ => SessionActor.Unknown,
        };
    }

    private static bool IsTextContentItem(JsonElement contentItem)
    {
        var contentType = TryGetString(contentItem, "type");
        return contentType is "input_text" or "output_text"
            && contentItem.TryGetProperty("text", out var textElement)
            && !string.IsNullOrWhiteSpace(textElement.GetString());
    }

    private static string? TryExtractCommand(string rawArguments)
    {
        if (rawArguments is null)
        {
            throw new ArgumentNullException(nameof(rawArguments));
        }

        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawArguments);
        if (!document.RootElement.TryGetProperty("cmd", out var commandElement)
            || commandElement.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return commandElement.GetString();
    }

    private static void ExtractFilePathsAndUrls(string value, ISet<string> filePaths, ISet<string> urls)
    {
        var nonNullValue = value ?? throw new ArgumentNullException(nameof(value));
        var nonNullFilePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
        var nonNullUrls = urls ?? throw new ArgumentNullException(nameof(urls));

        foreach (Match match in UrlRegex.Matches(nonNullValue))
        {
            var urlValue = match.Value;
            nonNullUrls.Add(urlValue);
        }

        foreach (Match match in FilePathRegex.Matches(nonNullValue))
        {
            var filePathValue = match.Value;
            nonNullFilePaths.Add(filePathValue);
        }
    }

    private static bool TryExtractExitCode(string text, out int exitCode)
    {
        var sourceText = text ?? throw new ArgumentNullException(nameof(text));

        exitCode = 0;
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }
        const string marker = "Process exited with code ";
        var index = sourceText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var numberPortion = sourceText[(index + marker.Length)..].Trim();
        var numericValue = new string(numberPortion.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(numericValue, out exitCode);
    }

    [GeneratedRegex(@"https?://[^\s`""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegexFactory();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s`""']+", RegexOptions.Compiled)]
    private static partial Regex FilePathRegexFactory();

    private sealed class ParseState
    {
        public string? SessionId { get; set; }

        public string? ForkedFromId { get; set; }

        public string? Cwd { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.MinValue;

        public List<NormalizedSessionEvent> Events { get; } = [];

        public List<string> Commands { get; } = [];

        public List<int> ExitCodes { get; } = [];

        public HashSet<string> FilePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Urls { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

