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

        var sessionFilePath = filePath;
        var lines = await File.ReadAllLinesAsync(sessionFilePath, cancellationToken);
        var state = new ParseState();

        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            ParseLine(document.RootElement, state);
        }

        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            throw new InvalidOperationException($"Session ID was not found in {sessionFilePath}.");
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
        var parseState = RequireState(state);
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var type = TryGetString(root, "type");
        switch (type)
        {
            case "session_meta" when root.TryGetProperty("payload", out var sessionMetaPayload):
                ParseSessionMetadata(sessionMetaPayload, parseState);
                break;
            case "response_item" when root.TryGetProperty("payload", out var responseItemPayload):
                ParseResponseItem(responseItemPayload, parseState);
                break;
            default:
                return;
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        var parseState = RequireState(state);
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var sessionId = TryGetString(payload, "id");
        if (parseState.SessionId is null && !string.IsNullOrWhiteSpace(sessionId))
        {
            parseState.SessionId = sessionId;
        }

        var forkedFromId = TryGetString(payload, "forked_from_id");
        if (parseState.ForkedFromId is null && !string.IsNullOrWhiteSpace(forkedFromId))
        {
            parseState.ForkedFromId = forkedFromId;
        }

        var cwd = TryGetString(payload, "cwd");
        if (parseState.Cwd is null && !string.IsNullOrWhiteSpace(cwd))
        {
            parseState.Cwd = cwd;
        }

        if (parseState.StartedAtUtc == DateTimeOffset.MinValue
            && payload.TryGetProperty("timestamp", out var timestampElement)
            && DateTimeOffset.TryParse(timestampElement.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStartedAt))
        {
            parseState.StartedAtUtc = parsedStartedAt;
        }
    }

    private static void ParseResponseItem(JsonElement payload, ParseState state)
    {
        var parseState = RequireState(state);
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var payloadType = TryGetString(payload, "type");
        switch (payloadType)
        {
            case "message":
                ParseMessage(payload, parseState);
                break;
            case "function_call":
                ParseFunctionCall(payload, parseState);
                break;
            case "function_call_output":
                ParseFunctionCallOutput(payload, parseState);
                break;
            default:
                return;
        }
    }

    private static void ParseMessage(JsonElement payload, ParseState state)
    {
        var parseState = RequireState(state);
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        if (!payload.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        var actor = ResolveActor(TryGetString(payload, "role"));
        var events = parseState.Events;
        var filePaths = parseState.FilePaths;
        var urls = parseState.Urls;
        foreach (var contentItem in contentElement.EnumerateArray())
        {
            if (!IsTextContentItem(contentItem))
            {
                continue;
            }

            var text = contentItem.GetProperty("text").GetString()!;
            events.Add(NormalizedSessionEvent.CreateMessage(actor, text));
            ExtractFilePathsAndUrls(text, filePaths, urls);
        }
    }

    private static void ParseFunctionCall(JsonElement payload, ParseState state)
    {
        var parseState = RequireState(state);
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var rawToolName = TryGetString(payload, "name");
        var toolName = string.IsNullOrWhiteSpace(rawToolName) ? "unknown_tool" : rawToolName;
        var rawArgumentsText = TryGetString(payload, "arguments");
        var rawArguments = string.IsNullOrWhiteSpace(rawArgumentsText) ? string.Empty : rawArgumentsText;
        parseState.Events.Add(NormalizedSessionEvent.CreateToolCall(toolName, rawArguments));

        var command = TryExtractCommand(rawArguments);
        if (!string.IsNullOrWhiteSpace(command))
        {
            parseState.Commands.Add(command!);
        }

        ExtractFilePathsAndUrls(rawArguments, parseState.FilePaths, parseState.Urls);
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        var parseState = RequireState(state);
        if (payload.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var outputText = TryGetString(payload, "output") ?? string.Empty;
        var rawToolName = TryGetString(payload, "name");
        var toolName = string.IsNullOrWhiteSpace(rawToolName) ? "tool" : rawToolName;
        parseState.Events.Add(NormalizedSessionEvent.CreateToolOutput(toolName, outputText));

        if (TryExtractExitCode(outputText, out var exitCode))
        {
            parseState.ExitCodes.Add(exitCode);
        }

        ExtractFilePathsAndUrls(outputText, parseState.FilePaths, parseState.Urls);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var propertyElement))
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
        if (contentItem.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var contentType = TryGetString(contentItem, "type");
        return contentType is "input_text" or "output_text"
            && contentItem.TryGetProperty("text", out var textElement)
            && !string.IsNullOrWhiteSpace(textElement.GetString());
    }

    private static string? TryExtractCommand(string rawArguments)
    {
        ArgumentNullException.ThrowIfNull(rawArguments);

        var argumentsText = rawArguments.Trim();
        if (argumentsText.Length == 0 || argumentsText[0] != '{')
        {
            return null;
        }

        using var document = JsonDocument.Parse(argumentsText);
        if (!document.RootElement.TryGetProperty("cmd", out var commandElement)
            || commandElement.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return commandElement.GetString();
    }

    private static void ExtractFilePathsAndUrls(string value, ISet<string> filePaths, ISet<string> urls)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(urls);

        foreach (Match match in UrlRegex.Matches(value))
        {
            urls.Add(match.Value);
        }

        foreach (Match match in FilePathRegex.Matches(value))
        {
            filePaths.Add(match.Value);
        }
    }

    private static bool TryExtractExitCode(string text, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(text);

        exitCode = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        const string marker = "Process exited with code ";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var numberPortion = text[(index + marker.Length)..].Trim();
        var numericValue = new string(numberPortion.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(numericValue, out exitCode);
    }

    private static ParseState RequireState(ParseState? state) =>
        state ?? throw new ArgumentNullException(nameof(state));

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

