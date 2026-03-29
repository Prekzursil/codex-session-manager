#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Maintenance;

public sealed class MaintenanceExecutor
{
    private const string NullOrWhitespaceMessage = "Value cannot be null or whitespace.";
    private readonly string _checkpointRoot;

    public MaintenanceExecutor(string checkpointRoot)
    {
        if (string.IsNullOrWhiteSpace(checkpointRoot))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(checkpointRoot));
        }

        _checkpointRoot = checkpointRoot;
    }

    public async Task<MaintenanceExecutionResult> ExecuteAsync(
        MaintenancePreview preview,
        string destinationRoot,
        string typedConfirmation,
        CancellationToken cancellationToken)
    {
        var checkedPreview = preview ?? throw new ArgumentNullException(nameof(preview));
        var checkedDestinationRoot = destinationRoot ?? throw new ArgumentNullException(nameof(destinationRoot));
        var checkedTypedConfirmation = typedConfirmation ?? throw new ArgumentNullException(nameof(typedConfirmation));
        var action = checkedPreview.Action;

        ValidateTypedConfirmation(checkedPreview, checkedTypedConfirmation);

        var effectiveDestinationRoot = PrepareDestinationRoot(action, checkedDestinationRoot);
        var movedTargets = MoveTargets(checkedPreview.AllowedTargets, effectiveDestinationRoot, cancellationToken);
        var manifestPath = await WriteManifestAsync(action, movedTargets, cancellationToken);

        return new MaintenanceExecutionResult(
            Executed: true,
            MovedTargets: movedTargets,
            ManifestPath: manifestPath);
    }

    private static void ValidateTypedConfirmation(MaintenancePreview preview, string typedConfirmation)
    {
        if (!preview.RequiresTypedConfirmation || string.IsNullOrWhiteSpace(typedConfirmation))
        {
            throw new InvalidOperationException("Typed confirmation is required.");
        }

        if (!string.Equals(preview.RequiredTypedConfirmation, typedConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Typed confirmation does not match the preview.");
        }
    }

    private string PrepareDestinationRoot(MaintenanceAction action, string destinationRoot)
    {
        Directory.CreateDirectory(_checkpointRoot);
        var effectiveDestinationRoot = GetEffectiveDestinationRoot(action, destinationRoot);
        Directory.CreateDirectory(effectiveDestinationRoot);
        return effectiveDestinationRoot;
    }

    private string GetEffectiveDestinationRoot(MaintenanceAction action, string destinationRoot) =>
        action switch
        {
            MaintenanceAction.Delete => Path.Combine(_checkpointRoot, "deleted"),
            MaintenanceAction.Reconcile => Path.Combine(destinationRoot, "reconciled"),
            _ => destinationRoot
        };

    private static List<SessionPhysicalCopy> MoveTargets(
        IReadOnlyList<SessionPhysicalCopy> allowedTargets,
        string effectiveDestinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedTargets);
        var targets = allowedTargets;
        if (string.IsNullOrWhiteSpace(effectiveDestinationRoot))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(effectiveDestinationRoot));
        }

        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in targets)
        {
            ArgumentNullException.ThrowIfNull(target);
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(target.FilePath);
            var destinationPath = BuildDestinationPath(effectiveDestinationRoot, fileName);
            Directory.CreateDirectory(effectiveDestinationRoot);
            File.Move(target.FilePath, destinationPath);
            movedTargets.Add(target with { FilePath = destinationPath });
        }

        return movedTargets;
    }

    private static string BuildDestinationPath(string effectiveDestinationRoot, string fileName)
    {
        if (string.IsNullOrWhiteSpace(effectiveDestinationRoot))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(effectiveDestinationRoot));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(fileName));
        }

        var destinationPath = Path.Combine(effectiveDestinationRoot, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        return Path.Combine(effectiveDestinationRoot, uniqueName);
    }

    private async Task<string> WriteManifestAsync(
        MaintenanceAction action,
        IReadOnlyList<SessionPhysicalCopy> movedTargets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movedTargets);

        var targets = movedTargets;
        var manifestPath = Path.Combine(_checkpointRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{action}.json");
        var payload = new
        {
            action = action.ToString(),
            executedAtUtc = DateTimeOffset.UtcNow,
            targets = targets.Select(target => new
            {
                sessionId = target.SessionId,
                filePath = target.FilePath,
                storeKind = target.StoreKind.ToString()
            })
        };
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return manifestPath;
    }
}

