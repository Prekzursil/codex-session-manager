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

    public static Task<ParsedSessionFile> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lines = File.ReadAllLines(filePath);
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

        var parsedSessionFile = new ParsedSessionFile(
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
        return Task.FromResult(parsedSessionFile);
    }

    private static void ParseLine(JsonElement root, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var type = TryGetString(root, "type");
        if (!TryGetPayload(root, out var payload))
        {
            return;
        }

        if (type is "session_meta")
        {
            ParseSessionMetadata(payload, state);
            return;
        }

        if (type is "response_item")
        {
            ParseResponseItem(payload, state);
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        state.SetMetadataIfMissing(
            TryGetString(payload, "id"),
            TryGetString(payload, "forked_from_id"),
            TryGetString(payload, "cwd"),
            TryGetTimestamp(payload));
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

        var actor = ResolveActor(TryGetString(payload, "role"));
        foreach (var contentItem in contentElement.EnumerateArray())
        {
            if (!IsTextContentItem(contentItem))
            {
                continue;
            }

            var text = contentItem.GetProperty("text").GetString()!;
            state.AddMessage(actor, text);
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
        state.AddToolCall(toolName, rawArguments);
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var outputText = TryGetString(payload, "output") ?? string.Empty;
        var toolName = TryGetString(payload, "name") ?? "tool";
        state.AddToolOutput(toolName, outputText);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
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
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

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

        public void SetMetadataIfMissing(string? sessionId, string? forkedFromId, string? cwd, DateTimeOffset? startedAtUtc)
        {
            SessionId ??= sessionId;
            ForkedFromId ??= forkedFromId;
            Cwd ??= cwd;
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
                Commands.Add(command!);
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
    }
}

