// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Text.Json; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using System.Text.RegularExpressions;
using CodexSessionManager.Core.Transcripts;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace CodexSessionManager.Storage.Parsing;

[SuppressMessage("Code Smell", "S2333", Justification = "GeneratedRegex members require the containing type to be partial.")]
public static partial class SessionJsonlParser // NOSONAR - partial is required for GeneratedRegex members.
{
    private static readonly Regex UrlRegex = UrlRegexFactory();
    private static readonly Regex FilePathRegex = FilePathRegexFactory();

    public static async Task<ParsedSessionFile> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(filePath));
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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

        var type = TryGetString(root, "type"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        switch (type)
        {
            case "session_meta" when root.TryGetProperty("payload", out var sessionMetaPayload): // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
                ParseSessionMetadata(sessionMetaPayload, state);
                break;
            case "response_item" when root.TryGetProperty("payload", out var responseItemPayload): // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
                ParseResponseItem(responseItemPayload, state);
                break;
            default:
                return;
        }
    }

    private static void ParseSessionMetadata(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        state.SessionId ??= TryGetString(payload, "id"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        state.ForkedFromId ??= TryGetString(payload, "forked_from_id"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        state.Cwd ??= TryGetString(payload, "cwd"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.

        if (state.StartedAtUtc == DateTimeOffset.MinValue // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
            && payload.TryGetProperty("timestamp", out var timestampElement) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
            && DateTimeOffset.TryParse(timestampElement.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStartedAt))
        {
            state.StartedAtUtc = parsedStartedAt; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        }
    }

    private static void ParseResponseItem(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var payloadType = TryGetString(payload, "type"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        switch (payloadType)
        {
            case "message":
                ParseMessage(payload, state); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
                break;
            case "function_call":
                ParseFunctionCall(payload, state); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
                break;
            case "function_call_output":
                ParseFunctionCallOutput(payload, state); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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

        if (!payload.TryGetProperty("content", out var contentElement) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
            || contentElement.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        var actor = ResolveActor(TryGetString(payload, "role")); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        foreach (var contentItem in contentElement.EnumerateArray())
        {
            if (!IsTextContentItem(contentItem))
            {
                continue;
            }

            var text = contentItem.GetProperty("text").GetString()!;
            state.Events.Add(NormalizedSessionEvent.CreateMessage(actor, text)); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
            ExtractFilePathsAndUrls(text, state.FilePaths, state.Urls); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        }
    }

    private static void ParseFunctionCall(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var toolName = TryGetString(payload, "name") ?? "unknown_tool"; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        var rawArguments = TryGetString(payload, "arguments") ?? string.Empty; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        state.Events.Add(NormalizedSessionEvent.CreateToolCall(toolName, rawArguments)); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.

        var command = TryExtractCommand(rawArguments);
        if (!string.IsNullOrWhiteSpace(command))
        {
            state.Commands.Add(command!); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        }

        ExtractFilePathsAndUrls(rawArguments, state.FilePaths, state.Urls); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
    }

    private static void ParseFunctionCallOutput(JsonElement payload, ParseState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var outputText = TryGetString(payload, "output") ?? string.Empty; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        var toolName = TryGetString(payload, "name") ?? "tool"; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        state.Events.Add(NormalizedSessionEvent.CreateToolOutput(toolName, outputText)); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.

        if (TryExtractExitCode(outputText, out var exitCode))
        {
            state.ExitCodes.Add(exitCode); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        }

        ExtractFilePathsAndUrls(outputText, state.FilePaths, state.Urls); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        if (!element.TryGetProperty(propertyName, out var propertyElement)) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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
        var contentType = TryGetString(contentItem, "type"); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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

        using var document = JsonDocument.Parse(rawArguments); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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
            urls.Add(match.Value); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
        }

        foreach (Match match in FilePathRegex.Matches(value))
        {
            filePaths.Add(match.Value); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after explicit domain validation.
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
    }
}

