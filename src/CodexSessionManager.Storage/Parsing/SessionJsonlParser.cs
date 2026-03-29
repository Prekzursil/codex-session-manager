using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Parsing;

[CLSCompliant(true)]
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

        var normalizedFilePath = filePath;
        var lines = await File.ReadAllLinesAsync(normalizedFilePath, cancellationToken);
        var state = new ParseState();

        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            ParseLine(document.RootElement, state);
        }

        var sessionId = state.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException($"Session ID was not found in {normalizedFilePath}.");
        }

        var startedAtUtc = state.StartedAtUtc == DateTimeOffset.MinValue
            ? DateTimeOffset.UtcNow
            : state.StartedAtUtc;
        return new ParsedSessionFile(
            SessionId: sessionId,
            ForkedFromId: state.ForkedFromId,
            Cwd: state.Cwd,
            TechnicalBreadcrumbs: new TechnicalBreadcrumbs(
                state.Commands,
                state.ExitCodes,
                state.FilePaths.ToArray(),
                state.Urls.ToArray()),
            Document: new NormalizedSessionDocument(
                sessionId,
                ThreadName: null,
                StartedAtUtc: startedAtUtc,
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

        var parseState = state;
        var type = TryGetString(root, "type");
        if (type == "session_meta")
        {
            if (TryGetPropertyValue(root, "payload", out var sessionMetaPayload))
            {
                ParseSessionMetadata(sessionMetaPayload, parseState);
            }

            return;
        }

        if (type == "response_item"
            && TryGetPropertyValue(root, "payload", out var responseItemPayload))
        {
            ParseResponseItem(responseItemPayload, parseState);
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var parseState = state;

        parseState.SessionId ??= TryGetString(payload, "id");
        parseState.ForkedFromId ??= TryGetString(payload, "forked_from_id");
        parseState.Cwd ??= TryGetString(payload, "cwd");

        if (parseState.StartedAtUtc == DateTimeOffset.MinValue
            && TryGetPropertyValue(payload, "timestamp", out var timestampElement)
            && DateTimeOffset.TryParse(timestampElement.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStartedAt))
        {
            parseState.StartedAtUtc = parsedStartedAt;
        }
    }

    private static void ParseResponseItem(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var parseState = state;
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
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var parseState = state;

        if (!TryGetPropertyValue(payload, "content", out var contentElement)
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
            if (!TryGetTextContent(contentItem, out var text))
            {
                continue;
            }

            events.Add(NormalizedSessionEvent.CreateMessage(actor, text));
            ExtractFilePathsAndUrls(text, filePaths, urls);
        }
    }

    private static void ParseFunctionCall(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var parseState = state;
        var toolName = TryGetString(payload, "name") ?? "unknown_tool";
        var rawArguments = TryGetString(payload, "arguments") ?? string.Empty;
        parseState.Events.Add(NormalizedSessionEvent.CreateToolCall(toolName, rawArguments));

        var command = TryExtractCommand(rawArguments);
        if (!string.IsNullOrWhiteSpace(command))
        {
            parseState.Commands.Add(command);
        }

        ExtractFilePathsAndUrls(rawArguments, parseState.FilePaths, parseState.Urls);
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var parseState = state;
        var outputText = TryGetString(payload, "output") ?? string.Empty;
        var toolName = TryGetString(payload, "name") ?? "tool";
        parseState.Events.Add(NormalizedSessionEvent.CreateToolOutput(toolName, outputText));

        if (TryExtractExitCode(outputText, out var exitCode))
        {
            parseState.ExitCodes.Add(exitCode);
        }

        ExtractFilePathsAndUrls(outputText, parseState.FilePaths, parseState.Urls);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        var jsonPropertyName = propertyName;
        if (!TryGetPropertyValue(element, jsonPropertyName, out var propertyElement))
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

    private static bool TryGetTextContent(JsonElement contentItem, out string text)
    {
        text = string.Empty;
        var contentType = TryGetString(contentItem, "type");
        if (contentType is not ("input_text" or "output_text"))
        {
            return false;
        }

        if (!TryGetPropertyValue(contentItem, "text", out var textElement))
        {
            return false;
        }

        var contentText = textElement.GetString();
        if (string.IsNullOrWhiteSpace(contentText))
        {
            return false;
        }

        text = contentText;
        return true;
    }

    private static string? TryExtractCommand(string rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawArguments);
        if (!TryGetPropertyValue(document.RootElement, "cmd", out var commandElement)
            || commandElement.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return commandElement.GetString();
    }

    private static void ExtractFilePathsAndUrls(string value, ISet<string> filePaths, ISet<string> urls)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (filePaths is null)
        {
            throw new ArgumentNullException(nameof(filePaths));
        }

        if (urls is null)
        {
            throw new ArgumentNullException(nameof(urls));
        }

        var sourceText = value;
        var filePathSet = filePaths;
        var urlSet = urls;

        foreach (Match match in UrlRegex.Matches(sourceText))
        {
            urlSet.Add(match.Value);
        }

        foreach (Match match in FilePathRegex.Matches(sourceText))
        {
            filePathSet.Add(match.Value);
        }
    }

    private static bool TryExtractExitCode(string text, out int exitCode)
    {
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

    private static bool TryGetPropertyValue(
        JsonElement element,
        string propertyName,
        out JsonElement propertyElement)
    {
        propertyElement = default;
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        var jsonPropertyName = propertyName;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return element.TryGetProperty(jsonPropertyName, out propertyElement);
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
