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
        if (!preview.RequiresTypedConfirmation || string.IsNullOrWhiteSpace(typedConfirmation))
        {
            throw new InvalidOperationException("Typed confirmation is required.");
        }

        if (!string.Equals(preview.RequiredTypedConfirmation, typedConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Typed confirmation does not match the preview.");
        }

        Directory.CreateDirectory(_checkpointRoot);
        var effectiveDestinationRoot = GetEffectiveDestinationRoot(preview.Action, destinationRoot);
        Directory.CreateDirectory(effectiveDestinationRoot);

        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in preview.AllowedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(target.FilePath);
            var destinationPath = Path.Combine(effectiveDestinationRoot, fileName);
            if (File.Exists(destinationPath))
            {
                var uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
                destinationPath = Path.Combine(effectiveDestinationRoot, uniqueName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(target.FilePath, destinationPath);
            movedTargets.Add(target with { FilePath = destinationPath });
        }

        var manifestPath = Path.Combine(_checkpointRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{preview.Action}.json");
        var payload = new
        {
            action = preview.Action.ToString(),
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

    private string GetEffectiveDestinationRoot(MaintenanceAction action, string destinationRoot) =>
        action switch
        {
            MaintenanceAction.Delete => Path.Combine(_checkpointRoot, "deleted"),
            MaintenanceAction.Reconcile => Path.Combine(destinationRoot, "reconciled"),
            _ => destinationRoot
        };
}
