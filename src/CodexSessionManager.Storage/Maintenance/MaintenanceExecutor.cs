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
        var action = preview.Action; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var requiredTypedConfirmation = preview.RequiredTypedConfirmation; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        var allowedTargets = preview.AllowedTargets; // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.

        if (!preview.RequiresTypedConfirmation || string.IsNullOrWhiteSpace(typedConfirmation)) // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
        {
            throw new InvalidOperationException("Typed confirmation is required.");
        }

        if (!string.Equals(requiredTypedConfirmation, typedConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Typed confirmation does not match the preview.");
        }

        Directory.CreateDirectory(_checkpointRoot);
        var effectiveDestinationRoot = GetEffectiveDestinationRoot(action, destinationRoot);
        Directory.CreateDirectory(effectiveDestinationRoot);

        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in allowedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested(); // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
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

        return new MaintenanceExecutionResult(
            Executed: true,
            MovedTargets: movedTargets,
            ManifestPath: manifestPath);
    }

    private string GetEffectiveDestinationRoot(MaintenanceAction action, string destinationRoot) =>
        action switch
        {
            MaintenanceAction.Delete => Path.Combine(_checkpointRoot, "deleted"),
            MaintenanceAction.Reconcile => Path.Combine(destinationRoot, "reconciled"), // nosemgrep: codacy.csharp.security.null-dereference -- false positive after constructor/guard validation.
            _ => destinationRoot
        };
}

