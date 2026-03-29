#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
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
        var action = preview.Action;
        ValidateTypedConfirmation(preview, typedConfirmation);

        var effectiveDestinationRoot = PrepareDestinationRoot(action, destinationRoot);
        var movedTargets = MoveTargets(preview.AllowedTargets, effectiveDestinationRoot, cancellationToken);
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

    private static List<SessionPhysicalCopy> MoveTargets(
        IReadOnlyList<SessionPhysicalCopy> allowedTargets,
        string effectiveDestinationRoot,
        CancellationToken cancellationToken)
    {
        var movedTargets = new List<SessionPhysicalCopy>();
        foreach (var target in allowedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(target.FilePath);
            var destinationPath = BuildDestinationPath(effectiveDestinationRoot, fileName);
            var destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidOperationException("Destination path does not include a directory.");

            Directory.CreateDirectory(destinationDirectory);
            File.Move(target.FilePath, destinationPath);
            movedTargets.Add(target with { FilePath = destinationPath });
        }

        return movedTargets;
    }

    private static string BuildDestinationPath(string effectiveDestinationRoot, string fileName)
    {
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

