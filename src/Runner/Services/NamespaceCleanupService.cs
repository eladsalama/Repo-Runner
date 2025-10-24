using k8s;
using k8s.Models;

namespace Runner.Services;

/// <summary>
/// Background service that sweeps expired namespaces based on TTL
/// </summary>
public class NamespaceCleanupService : BackgroundService
{
    private readonly ILogger<NamespaceCleanupService> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);

    public NamespaceCleanupService(
        ILogger<NamespaceCleanupService> logger,
        IKubernetes kubernetes)
    {
        _logger = logger;
        _kubernetes = kubernetes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Namespace cleanup service starting. Running every {Interval} minutes", _cleanupInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredNamespacesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during namespace cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupExpiredNamespacesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting namespace cleanup sweep");

        try
        {
            // List all namespaces managed by RepoRunner
            var namespaces = await _kubernetes.CoreV1.ListNamespaceAsync(
                labelSelector: "app.kubernetes.io/managed-by=reporunner",
                cancellationToken: cancellationToken);

            var now = DateTime.UtcNow;
            var deletedCount = 0;

            foreach (var ns in namespaces.Items)
            {
                try
                {
                    // Check delete-after annotation
                    if (ns.Metadata?.Annotations?.TryGetValue("reporunner.io/delete-after", out var deleteAfterStr) == true)
                    {
                        if (DateTime.TryParse(deleteAfterStr, out var deleteAfter) && now >= deleteAfter)
                        {
                            _logger.LogInformation(
                                "Deleting expired namespace {Namespace} (delete-after: {DeleteAfter})",
                                ns.Metadata.Name, deleteAfter);

                            await _kubernetes.CoreV1.DeleteNamespaceAsync(
                                ns.Metadata.Name,
                                cancellationToken: cancellationToken);

                            deletedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete namespace {Namespace}", ns.Metadata?.Name);
                }
            }

            _logger.LogInformation("Namespace cleanup sweep completed. Deleted {Count} namespace(s)", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list namespaces during cleanup");
            throw;
        }
    }
}
