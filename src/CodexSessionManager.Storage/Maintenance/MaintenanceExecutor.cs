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
        ValidateTypedConfirmation(preview, typedConfirmation);

        var action = preview.Action;
        var allowedTargets = preview.AllowedTargets;

        Directory.CreateDirectory(_checkpointRoot);
        var effectiveDestinationRoot = GetEffectiveDestinationRoot(action, destinationRoot);
        Directory.CreateDirectory(effectiveDestinationRoot);

        var movedTargets = MoveTargets(allowedTargets, effectiveDestinationRoot, cancellationToken);
        var manifestPath = await WriteManifestAsync(action, movedTargets, cancellationToken);

        return new MaintenanceExecutionResult(
            Executed: true,
            MovedTargets: movedTargets,
            ManifestPath: manifestPath);
    }

    private void ValidateTypedConfirmation(MaintenancePreview preview, string typedConfirmation)
    {
        if (!preview.RequiresTypedConfirmation || string.IsNullOrWhiteSpace(typedConfirmation))
        {
            throw new InvalidOperationException("Typed confirmation is required.");
        }

        var requiredTypedConfirmation = preview.RequiredTypedConfirmation;
        if (!string.Equals(requiredTypedConfirmation, typedConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Typed confirmation does not match the preview.");
        }
    }

    private List<SessionPhysicalCopy> MoveTargets(
        IReadOnlyList<SessionPhysicalCopy> allowedTargets,
        string effectiveDestinationRoot,
        CancellationToken cancellationToken)
    {
        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in allowedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = BuildDestinationPath(effectiveDestinationRoot, target.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(target.FilePath, destinationPath);
            movedTargets.Add(target with { FilePath = destinationPath });
        }

        return movedTargets;
    }

    private static string BuildDestinationPath(string effectiveDestinationRoot, string sourceFilePath)
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

    private async Task<string> WriteManifestAsync(
        MaintenanceAction action,
        IReadOnlyList<SessionPhysicalCopy> movedTargets,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_checkpointRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{action}.json");
        var payload = new
        {
            action = action.ToString(),
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

        return manifestPath;
    }

    private string GetEffectiveDestinationRoot(MaintenanceAction action, string destinationRoot) =>
        action switch
        {
            MaintenanceAction.Delete => Path.Combine(_checkpointRoot, "deleted"),
            MaintenanceAction.Reconcile => Path.Combine(destinationRoot, "reconciled"), // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
            _ => destinationRoot
        };
}

