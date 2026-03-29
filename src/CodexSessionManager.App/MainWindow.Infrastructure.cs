#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CodexSessionManager.Core.Sessions;
using CodexSessionManager.Storage.Discovery;

namespace CodexSessionManager.App;

[SuppressMessage("Code Smell", "S2333", Justification = "The class is split across XAML-generated and hand-authored partial files.")]
public partial class MainWindow
{
    private async Task RunOnUiThreadAsync(Action action)
    {
        var uiAction = action ?? throw new ArgumentNullException(nameof(action));

        if (Dispatcher is not { } dispatcher)
        {
            throw new InvalidOperationException("Dispatcher is unavailable.");
        }

        if (dispatcher.CheckAccess())
        {
            uiAction();
            return;
        }

        await dispatcher.InvokeAsync(uiAction);
    }

    private async Task<T> RunOnUiThreadValueAsync<T>(Func<T> func)
    {
        var uiFunc = func ?? throw new ArgumentNullException(nameof(func));

        if (Dispatcher is not { } dispatcher)
        {
            throw new InvalidOperationException("Dispatcher is unavailable.");
        }

        if (dispatcher.CheckAccess())
        {
            return uiFunc();
        }

        return await dispatcher.InvokeAsync(uiFunc);
    }

    private void RunEventTask(Func<Task> action, string failurePrefix)
    {
        var eventAction = action ?? throw new ArgumentNullException(nameof(action));

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
        var selectedSession = session ?? throw new ArgumentNullException(nameof(session));

        var preferredCopy = selectedSession.PreferredCopy;
        if (preferredCopy is null)
        {
            throw new InvalidOperationException("Selected session is missing a preferred copy.");
        }

        return preferredCopy;
    }

    internal static string? DescribeSqlitePath(string path)
    {
        var sqlitePath = path ?? throw new ArgumentNullException(nameof(path));
        return DescribeSqlitePath(sqlitePath, static candidate => new FileInfo(candidate));
    }

    internal static string? DescribeSqlitePath(
        string path,
        Func<string, FileInfo>? fileInfoFactory)
    {
        var sqlitePath = path ?? throw new ArgumentNullException(nameof(path));
        var createFileInfo = fileInfoFactory ?? static candidate => new FileInfo(candidate);
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
        var candidatePaths = sqlitePaths ?? throw new ArgumentNullException(nameof(sqlitePaths));
        var describePath = describeSqlitePath ?? throw new ArgumentNullException(nameof(describeSqlitePath));

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
        var provideKnownStores = knownStoresProvider ?? throw new ArgumentNullException(nameof(knownStoresProvider));

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
