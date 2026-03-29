#pragma warning disable S3990 // Codacy false positive: the assembly already declares CLSCompliant(true).
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CodexSessionManager.App;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Core.Transcripts;
using CodexSessionManager.Storage.Discovery;
using CodexSessionManager.Storage.Indexing;
using CodexSessionManager.Storage.Parsing;

namespace CodexSessionManager.App.Tests;

[SuppressMessage("Compatibility", "S3990", Justification = "The assembly already declares CLSCompliant(true); this file-level report is a persistent analyzer false positive.")]
[SuppressMessage("Code Smell", "S2333", Justification = "The coverage tests are intentionally split across partial files.")]
public sealed partial class MainWindowCoverageTests
{
    private static IReadOnlyList<KnownSessionStore> InvokeBuildKnownStores(bool deepScan) =>
        (IReadOnlyList<KnownSessionStore>)BuildKnownStoresMethod.Invoke(null, [deepScan])!;

    private static Task InvokePrivateTaskAsync(object instance, MethodInfo method, params object?[] args) =>
        (Task)method.Invoke(instance, args)!;

    private static T GetNamedField<T>(MainWindow window, string name) where T : class =>
        (typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as T)
        ?? throw new InvalidOperationException($"Field '{name}' was not found.");

    private static IndexedLogicalSession? GetSelectedSession(MainWindow window) =>
        typeof(MainWindow).GetMethod("GetSelectedSession", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(window, []) as IndexedLogicalSession;

    private static void AddSession(MainWindow window, IndexedLogicalSession session)
    {
        var sessions = (ObservableCollection<IndexedLogicalSession>)SessionsField.GetValue(window)!;
        sessions.Add(session);
    }

    private static void SelectSingleSession(MainWindow window, IndexedLogicalSession session)
    {
        var listBox = GetNamedField<ListBox>(window, "SessionsListBox");
        listBox.SelectedItem = session;
        listBox.SelectedItems.Clear();
        listBox.SelectedItems.Add(session);
    }

    private static SessionCatalogRepository CreateRepository(string root, params IndexedLogicalSession[] sessions)
    {
        var repository = new SessionCatalogRepository(Path.Combine(root, "catalog.db"));
        repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        foreach (var session in sessions)
        {
            repository.UpsertAsync(session, CancellationToken.None).GetAwaiter().GetResult();
        }

        return repository;
    }

    private static IndexedLogicalSession BuildIndexedSession(string sessionId, string threadName, string filePath) =>
        new(
            sessionId,
            threadName,
            new SessionPhysicalCopy(sessionId, filePath, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false)),
            [new SessionPhysicalCopy(sessionId, filePath, SessionStoreKind.Live, new SessionPhysicalCopyState(DateTimeOffset.UtcNow, 1024, false))],
            new SessionSearchDocument
            {
                ReadableTranscript = $"Readable transcript for {threadName}",
                DialogueTranscript = $"Dialogue transcript for {threadName}",
                ToolSummary = $"Tool summary for {threadName}",
                CommandText = "codex resume",
                FilePaths = [filePath],
                Urls = ["https://example.com"],
                ErrorText = string.Empty,
                Alias = string.Empty,
                Tags = [],
                Notes = string.Empty
            });

    private static ParsedSessionFile BuildParsedFile(string sessionId, string? cwd) =>
        new(
            sessionId,
            null,
            cwd,
            new TechnicalBreadcrumbs(["codex resume"], [0], [], []),
            new NormalizedSessionDocument(
                sessionId,
                "Thread",
                DateTimeOffset.UtcNow,
                null,
                cwd,
                [
                    NormalizedSessionEvent.CreateMessage(SessionActor.User, "Hello"),
                    NormalizedSessionEvent.CreateMessage(SessionActor.Assistant, "World")
                ]));

    private static IndexedLogicalSession WithNullIndexedSessionProperty(IndexedLogicalSession session, string propertyName)
    {
        var clone = session with { };
        typeof(IndexedLogicalSession).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(clone, null);
        return clone;
    }

    private static string WriteSessionJsonl(string root, string sessionId, string threadName)
    {
        var sessionsRoot = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        var filePath = Path.Combine(sessionsRoot, $"{sessionId}.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new
                {
                    id = sessionId,
                    cwd = root,
                    timestamp = "2026-03-26T00:00:00Z"
                }
            }),
            JsonSerializer.Serialize(new
            {
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = threadName
                        }
                    }
                }
            })
        };

        File.WriteAllLines(filePath, lines, Encoding.UTF8);
        return filePath;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "codex-session-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }

    private static void SetProvider(MainWindow window, string propertyName, object value) =>
        typeof(MainWindow).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(window, value);

    private static void RunInSta(Action action) =>
        RunInStaAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static void RunInSta(Func<Task> action) =>
        RunInStaAsync(action).GetAwaiter().GetResult();

    private static Task RunInStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (OperationCanceledException)
                {
                    completion.SetCanceled();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
