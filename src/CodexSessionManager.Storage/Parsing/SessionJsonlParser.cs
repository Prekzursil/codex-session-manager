using System.Text.Json;
using System.Text.RegularExpressions;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Storage.Parsing;

public static partial class SessionJsonlParser
{
    private static readonly Regex UrlRegex = UrlRegexFactory();
    private static readonly Regex FilePathRegex = FilePathRegexFactory();

    public static async Task<ParsedSessionFile> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);

        string? sessionId = null;
        string? forkedFromId = null;
        string? cwd = null;
        DateTimeOffset startedAtUtc = DateTimeOffset.MinValue;
        var events = new List<NormalizedSessionEvent>();
        var commands = new List<string>();
        var exitCodes = new List<int>();
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "session_meta":
                    var payload = root.GetProperty("payload");
                    sessionId ??= payload.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    forkedFromId ??= payload.TryGetProperty("forked_from_id", out var forkedElement) ? forkedElement.GetString() : null;
                    cwd ??= payload.TryGetProperty("cwd", out var cwdElement) ? cwdElement.GetString() : null;
                    startedAtUtc = payload.TryGetProperty("timestamp", out var timestampElement)
                        && DateTimeOffset.TryParse(timestampElement.GetString(), out var parsedStartedAt)
                        ? parsedStartedAt
                        : startedAtUtc;
                    break;

                case "response_item":
                    var payloadElement = root.GetProperty("payload");
                    var payloadType = payloadElement.TryGetProperty("type", out var payloadTypeElement)
                        ? payloadTypeElement.GetString()
                        : null;

                    if (payloadType is "message")
                    {
                        var role = payloadElement.GetProperty("role").GetString();
                        var actor = role switch
                        {
                            "user" => SessionActor.User,
                            "assistant" => SessionActor.Assistant,
                            "developer" => SessionActor.Developer,
                            "system" => SessionActor.System,
                            _ => SessionActor.Unknown
                        };

                        if (!payloadElement.TryGetProperty("content", out var contentElement)
                            || contentElement.ValueKind is not JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var contentItem in contentElement.EnumerateArray())
                        {
                            var contentType = contentItem.TryGetProperty("type", out var contentTypeElement)
                                ? contentTypeElement.GetString()
                                : null;
                            if (contentType is "input_text" or "output_text"
                                && contentItem.TryGetProperty("text", out var textElement)
                                && !string.IsNullOrWhiteSpace(textElement.GetString()))
                            {
                                var text = textElement.GetString()!;
                                events.Add(NormalizedSessionEvent.CreateMessage(actor, text));
                                ExtractFilePathsAndUrls(text, filePaths, urls);
                            }
                        }
                    }
                    else if (payloadType is "function_call")
                    {
                        var toolName = payloadElement.TryGetProperty("name", out var nameElement)
                            ? nameElement.GetString() ?? "unknown_tool"
                            : "unknown_tool";
                        var rawArguments = payloadElement.TryGetProperty("arguments", out var argsElement)
                            ? argsElement.GetString() ?? string.Empty
                            : string.Empty;
                        events.Add(NormalizedSessionEvent.CreateToolCall(toolName, rawArguments));

                        var command = TryExtractCommand(rawArguments);
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            commands.Add(command!);
                        }

                        ExtractFilePathsAndUrls(rawArguments, filePaths, urls);
                    }
                    else if (payloadType is "function_call_output")
                    {
                        var outputText = payloadElement.TryGetProperty("output", out var outputElement)
                            ? outputElement.GetString() ?? string.Empty
                            : string.Empty;
                        var toolName = payloadElement.TryGetProperty("name", out var outputNameElement)
                            ? outputNameElement.GetString() ?? "tool"
                            : "tool";
                        events.Add(NormalizedSessionEvent.CreateToolOutput(toolName, outputText));

                        if (TryExtractExitCode(outputText, out var exitCode))
                        {
                            exitCodes.Add(exitCode);
                        }

                        ExtractFilePathsAndUrls(outputText, filePaths, urls);
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException($"Session ID was not found in {filePath}.");
        }

        return new ParsedSessionFile(
            SessionId: sessionId,
            ForkedFromId: forkedFromId,
            Cwd: cwd,
            TechnicalBreadcrumbs: new TechnicalBreadcrumbs(commands, exitCodes, filePaths.ToArray(), urls.ToArray()),
            Document: new NormalizedSessionDocument(
                sessionId,
                ThreadName: null,
                StartedAtUtc: startedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : startedAtUtc,
                ForkedFromId: forkedFromId,
                Cwd: cwd,
                Events: events));
    }

    private static string? TryExtractCommand(string rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawArguments);
        return document.RootElement.TryGetProperty("cmd", out var commandElement)
            ? commandElement.GetString()
            : null;
    }

    private static void ExtractFilePathsAndUrls(string value, ISet<string> filePaths, ISet<string> urls)
    {
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
        exitCode = 0;
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
}
