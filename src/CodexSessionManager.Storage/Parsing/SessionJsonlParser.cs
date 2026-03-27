using System.Text.Json;
using System.Text.RegularExpressions;
using CodexSessionManager.Core.Transcripts;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Storage.Parsing;

[SuppressMessage("Code Smell", "S2333", Justification = "GeneratedRegex members require the containing type to be partial.")]
public static partial class SessionJsonlParser
{
    private const string UnknownToolName = "unknown_tool";
    private const string DefaultToolOutputName = "tool";
    private static readonly Regex UrlRegex = UrlRegexFactory();
    private static readonly Regex FilePathRegex = FilePathRegexFactory();

    public static async Task<ParsedSessionFile> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var sessionPath = RequireFilePath(filePath);

        var lines = await File.ReadAllLinesAsync(sessionPath, cancellationToken);
        var state = new ParseState();

        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            ParseLine(document.RootElement, state);
        }

        return state.CreateParsedSessionFile(sessionPath);
    }

    private static void ParseLine(JsonElement root, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var type = TryGetString(root, "type");
        if (type is "session_meta" && TryGetPayload(root, out var sessionMetaPayload))
        {
            ParseSessionMetadata(sessionMetaPayload, state);
            return;
        }

        if (type is "response_item" && TryGetPayload(root, out var responseItemPayload))
        {
            ParseResponseItem(responseItemPayload, state);
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.SetSessionIdIfMissing(TryGetString(payload, "id"));
        state.SetForkedFromIdIfMissing(TryGetString(payload, "forked_from_id"));
        state.SetCwdIfMissing(TryGetString(payload, "cwd"));
        state.SetStartedAtIfMissing(TryGetTimestamp(payload));
    }

    private static void ParseResponseItem(JsonElement payload, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        switch (TryGetString(payload, "type"))
        {
            case "message":
                ParseMessage(payload, state);
                break;
            case "function_call":
                ParseFunctionCall(payload, state);
                break;
            case "function_call_output":
                ParseFunctionCallOutput(payload, state);
                break;
            default:
                return;
        }
    }

    private static void ParseMessage(JsonElement payload, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryGetContentArray(payload, out var contentElement))
        {
            return;
        }

        var actor = ResolveActor(TryGetString(payload, "role"));
        foreach (var contentItem in contentElement.EnumerateArray())
        {
            if (!IsTextContentItem(contentItem))
            {
                continue;
            }

            state.AddMessage(actor, contentItem.GetProperty("text").GetString()!);
        }
    }

    private static void ParseFunctionCall(JsonElement payload, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var toolName = TryGetString(payload, "name") ?? UnknownToolName;
        var rawArguments = TryGetString(payload, "arguments") ?? string.Empty;
        state.AddToolCall(toolName, rawArguments);
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var outputText = TryGetString(payload, "output") ?? string.Empty;
        var toolName = TryGetString(payload, "name") ?? DefaultToolOutputName;
        state.AddToolOutput(toolName, outputText);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

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
        var contentType = TryGetString(contentItem, "type");
        return contentType is "input_text" or "output_text"
            && contentItem.TryGetProperty("text", out var textElement)
            && !string.IsNullOrWhiteSpace(textElement.GetString());
    }

    private static string? TryExtractCommand(string rawArguments)
    {
        ArgumentNullException.ThrowIfNull(rawArguments);

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

    private static string RequireFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
        }

        return filePath;
    }

    private static bool TryGetPayload(JsonElement root, out JsonElement payload)
    {
        payload = default;
        return root.TryGetProperty("payload", out payload);
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement payload)
    {
        if (!payload.TryGetProperty("timestamp", out var timestampElement))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            timestampElement.GetString() ?? string.Empty,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsedStartedAt)
            ? parsedStartedAt
            : null;
    }

    private static bool TryGetContentArray(JsonElement payload, out JsonElement contentElement)
    {
        contentElement = default;
        return payload.TryGetProperty("content", out contentElement)
            && contentElement.ValueKind is JsonValueKind.Array;
    }

    [GeneratedRegex(@"https?://[^\s`""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegexFactory();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s`""']+", RegexOptions.Compiled)]
    private static partial Regex FilePathRegexFactory();

    private sealed class ParseState
    {
        public string? SessionId { get; private set; }

        public string? ForkedFromId { get; private set; }

        public string? Cwd { get; private set; }

        public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.MinValue;

        public List<NormalizedSessionEvent> Events { get; } = [];

        public List<string> Commands { get; } = [];

        public List<int> ExitCodes { get; } = [];

        public HashSet<string> FilePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Urls { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void SetSessionIdIfMissing(string? sessionId)
        {
            SessionId ??= sessionId;
        }

        public void SetForkedFromIdIfMissing(string? forkedFromId)
        {
            ForkedFromId ??= forkedFromId;
        }

        public void SetCwdIfMissing(string? cwd)
        {
            Cwd ??= cwd;
        }

        public void SetStartedAtIfMissing(DateTimeOffset? startedAtUtc)
        {
            if (StartedAtUtc == DateTimeOffset.MinValue && startedAtUtc.HasValue)
            {
                StartedAtUtc = startedAtUtc.Value;
            }
        }

        public void AddMessage(SessionActor actor, string text)
        {
            Events.Add(NormalizedSessionEvent.CreateMessage(actor, text));
            ExtractFilePathsAndUrls(text, FilePaths, Urls);
        }

        public void AddToolCall(string toolName, string rawArguments)
        {
            Events.Add(NormalizedSessionEvent.CreateToolCall(toolName, rawArguments));
            var command = TryExtractCommand(rawArguments);
            if (!string.IsNullOrWhiteSpace(command))
            {
                Commands.Add(command);
            }

            ExtractFilePathsAndUrls(rawArguments, FilePaths, Urls);
        }

        public void AddToolOutput(string toolName, string outputText)
        {
            Events.Add(NormalizedSessionEvent.CreateToolOutput(toolName, outputText));
            if (TryExtractExitCode(outputText, out var exitCode))
            {
                ExitCodes.Add(exitCode);
            }

            ExtractFilePathsAndUrls(outputText, FilePaths, Urls);
        }

        public ParsedSessionFile CreateParsedSessionFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(SessionId))
            {
                throw new InvalidOperationException($"Session ID was not found in {filePath}.");
            }

            return new ParsedSessionFile(
                SessionId,
                ForkedFromId,
                Cwd,
                new TechnicalBreadcrumbs(Commands, ExitCodes, FilePaths.ToArray(), Urls.ToArray()),
                new NormalizedSessionDocument(
                    SessionId,
                    ThreadName: null,
                    StartedAtUtc: StartedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : StartedAtUtc,
                    ForkedFromId,
                    Cwd,
                    Events));
        }
    }
}

