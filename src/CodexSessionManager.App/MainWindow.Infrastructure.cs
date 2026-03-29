using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;

namespace CodexSessionManager.App;

[SuppressMessage("Compatibility", "S3990", Justification = "The assembly already declares CLSCompliant(true); this file-level report is a persistent analyzer false positive.")]
[SuppressMessage("Code Smell", "S2333", Justification = "The class is split across XAML-generated and hand-authored partial files.")]
public partial class MainWindow
{
    private async Task RunOnUiThreadAsync(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var uiAction = action;
        var dispatcher = Dispatcher;

        if (dispatcher.CheckAccess())
        {
            uiAction();
            return;
        }

        await dispatcher.InvokeAsync(uiAction);
    }

    private async Task<T> RunOnUiThreadValueAsync<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        var uiFunc = func;
        var dispatcher = Dispatcher;

        if (dispatcher.CheckAccess())
        {
            return uiFunc();
        }

        return await dispatcher.InvokeAsync(uiFunc);
    }

    private void RunEventTask(Func<Task> action, string failurePrefix)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var eventAction = action;

        if (string.IsNullOrWhiteSpace(failurePrefix))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(failurePrefix));
        }

        _ = RunEventTaskCoreAsync();

        async Task RunEventTaskCoreAsync()
        {
            try
            {
                await eventAction();
            }
            catch (Exception ex)
            {
                await RunOnUiThreadAsync(() => StatusTextBlock.Text = $"{failurePrefix}: {ex.Message}");
            }
        }
    }

    private static List<KnownSessionStore> BuildKnownStores(bool deepScan)
    {
        var codexHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        var stores = new List<KnownSessionStore>(KnownStoreLocator.GetKnownStores(codexHome));
        if (!deepScan)
        {
            return stores;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var directory in Directory.EnumerateDirectories(
                     userProfile,
                     ".codex*",
                     SearchOption.TopDirectoryOnly))
        {
            foreach (var store in KnownStoreLocator.GetKnownStores(directory)
                         .Where(store => stores.All(existing =>
                             !string.Equals(
                                 existing.SessionsPath,
                                 store.SessionsPath,
                                 StringComparison.OrdinalIgnoreCase))))
            {
                stores.Add(store);
            }
        }

        return stores;
    }

    private static SessionPhysicalCopy GetRequiredPreferredCopy(IndexedLogicalSession? session)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var selectedSession = session;

        var preferredCopy = selectedSession.PreferredCopy;
        if (preferredCopy is null)
        {
            throw new InvalidOperationException("Selected session is missing a preferred copy.");
        }

        return preferredCopy;
    }

    internal static string? DescribeSqlitePath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var sqlitePath = path;
        return DescribeSqlitePath(sqlitePath, static candidate => new FileInfo(candidate));
    }

    internal static string? DescribeSqlitePath(
        string path,
        Func<string, FileInfo>? fileInfoFactory)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var sqlitePath = path;
        var createFileInfo = fileInfoFactory;
        if (createFileInfo is null)
        {
            createFileInfo = static candidate => new FileInfo(candidate);
        }

        try
        {
            var info = createFileInfo(sqlitePath);
            if (!info.Exists)
            {
                return null;
            }

            return $"{sqlitePath} | {Math.Round(info.Length / 1024.0 / 1024.0, 1)} MB | {info.LastWriteTime}";
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetLiveSqliteStatus()
    {
        var codexHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        return GetLiveSqliteStatus(
            [
                Path.Combine(codexHome, "state_5.sqlite"),
                Path.Combine(codexHome, "codex-sqlite", "canonical", "state_5.sqlite"),
            ],
            DescribeSqlitePath);
    }

    private static string GetLiveSqliteStatus(
        IEnumerable<string> sqlitePaths,
        Func<string, string?> describeSqlitePath)
    {
        if (sqlitePaths is null)
        {
            throw new ArgumentNullException(nameof(sqlitePaths));
        }

        if (describeSqlitePath is null)
        {
            throw new ArgumentNullException(nameof(describeSqlitePath));
        }

        var candidatePaths = sqlitePaths;
        var describePath = describeSqlitePath;

        var details = candidatePaths
            .Select(describePath)
            .Where(detail => detail is not null)
            .Cast<string>()
            .ToArray();

        return details.Length == 0
            ? "No live SQLite store detected."
            : string.Join(Environment.NewLine, details);
    }

    private static IReadOnlyList<KnownSessionStore> GetKnownStores(
        Func<bool, IReadOnlyList<KnownSessionStore>> knownStoresProvider,
        bool deepScan)
    {
        if (knownStoresProvider is null)
        {
            throw new ArgumentNullException(nameof(knownStoresProvider));
        }

        var provideKnownStores = knownStoresProvider;

        var knownStores = provideKnownStores(deepScan);
        if (knownStores is null)
        {
            throw new InvalidOperationException("Known stores provider returned no stores.");
        }

        return knownStores;
    }

    private sealed class SearchCancellationState : IDisposable
    {
        private CancellationTokenSource? _current;

        public CancellationToken Begin()
        {
            var replacement = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _current, replacement);
            previous?.Cancel();
            previous?.Dispose();
            return replacement.Token;
        }

        public CancellationTokenSource? Snapshot() => Volatile.Read(ref _current);

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _current, null);
            current?.Cancel();
            current?.Dispose();
        }
    }
}
