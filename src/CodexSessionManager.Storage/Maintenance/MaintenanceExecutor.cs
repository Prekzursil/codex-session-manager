using System.Text.Json;
using CodexSessionManager.Core.Maintenance;
using CodexSessionManager.Core.Sessions;

namespace CodexSessionManager.Storage.Maintenance;

public sealed class MaintenanceExecutor
{
    private readonly string _checkpointRoot;

    public MaintenanceExecutor(string checkpointRoot)
    {
        _checkpointRoot = checkpointRoot;
    }

    public async Task<MaintenanceExecutionResult> ExecuteAsync(
        MaintenancePreview preview,
        string destinationRoot,
        string typedConfirmation,
        CancellationToken cancellationToken)
    {
        var executionPlan = CreateExecutionPlan(preview, destinationRoot, typedConfirmation);

        Directory.CreateDirectory(_checkpointRoot);
        Directory.CreateDirectory(executionPlan.EffectiveDestinationRoot);

        var movedTargets = MoveTargets(executionPlan.AllowedTargets, executionPlan.EffectiveDestinationRoot, cancellationToken);

        var manifestPath = Path.Combine(_checkpointRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{executionPlan.Action}.json");
        var payload = new
        {
            action = executionPlan.Action.ToString(),
            executedAtUtc = DateTimeOffset.UtcNow,
            targets = movedTargets.Select(target => new
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

        return new MaintenanceExecutionResult(
            Executed: true,
            MovedTargets: movedTargets,
            ManifestPath: manifestPath);
    }

    private ExecutionPlan CreateExecutionPlan(MaintenancePreview preview, string destinationRoot, string typedConfirmation)
    {
        ArgumentNullException.ThrowIfNull(preview);

        EnsureTypedConfirmation(preview.RequiresTypedConfirmation, preview.RequiredTypedConfirmation, typedConfirmation);
        return new ExecutionPlan(
            preview.Action,
            preview.AllowedTargets,
            GetEffectiveDestinationRoot(preview.Action, destinationRoot));
    }

    private static void EnsureTypedConfirmation(bool requiresTypedConfirmation, string requiredTypedConfirmation, string typedConfirmation)
    {
        if (!requiresTypedConfirmation || string.IsNullOrWhiteSpace(typedConfirmation))
        {
            throw new InvalidOperationException("Typed confirmation is required.");
        }

        if (!string.Equals(requiredTypedConfirmation, typedConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Typed confirmation does not match the preview.");
        }
    }

    private static List<SessionPhysicalCopy> MoveTargets(
        IReadOnlyList<SessionPhysicalCopy> allowedTargets,
        string effectiveDestinationRoot,
        CancellationToken cancellationToken)
    {
        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in allowedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = ResolveDestinationPath(effectiveDestinationRoot, target.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(target.FilePath, destinationPath);
            movedTargets.Add(target with { FilePath = destinationPath });
        }

        return movedTargets;
    }

    private static string ResolveDestinationPath(string effectiveDestinationRoot, string sourceFilePath)
    {
        var fileName = Path.GetFileName(sourceFilePath);
        var destinationPath = Path.Combine(effectiveDestinationRoot, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        return Path.Combine(effectiveDestinationRoot, uniqueName);
    }

    private string GetEffectiveDestinationRoot(MaintenanceAction action, string destinationRoot) =>
        action switch
        {
            MaintenanceAction.Delete => Path.Combine(_checkpointRoot, "deleted"),
            MaintenanceAction.Reconcile => Path.Combine(destinationRoot, "reconciled"),
            _ => destinationRoot
        };

    private readonly record struct ExecutionPlan(
        MaintenanceAction Action,
        IReadOnlyList<SessionPhysicalCopy> AllowedTargets,
        string EffectiveDestinationRoot);
}

