using k8s;
using Shared.Repositories;
using Shared.Models;

namespace Runner.Services;

/// <summary>
/// Service to tail pod logs and write them to MongoDB
/// </summary>
public interface IPodLogTailer
{
    Task TailLogsAsync(string runId, string namespaceName, string? serviceName, CancellationToken cancellationToken = default);
}

public class PodLogTailer : IPodLogTailer
{
    private readonly IKubernetes _k8sClient;
    private readonly ILogRepository _logRepository;
    private readonly ILogger<PodLogTailer> _logger;

    public PodLogTailer(
        IKubernetes k8sClient,
        ILogRepository logRepository,
        ILogger<PodLogTailer> logger)
    {
        _k8sClient = k8sClient;
        _logRepository = logRepository;
        _logger = logger;
    }

    public async Task TailLogsAsync(string runId, string namespaceName, string? serviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            // List pods in namespace
            var pods = await _k8sClient.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                cancellationToken: cancellationToken);

            var tasks = new List<Task>();

            foreach (var pod in pods.Items)
            {
                // Get service name from pod labels
                var podServiceName = pod.Metadata.Labels.ContainsKey("app")
                    ? pod.Metadata.Labels["app"]
                    : null;

                // If filtering by service and this pod doesn't match, skip
                if (serviceName != null && podServiceName != serviceName)
                {
                    continue;
                }

                // Tail logs for each container in the pod
                foreach (var container in pod.Spec.Containers)
                {
                    tasks.Add(TailPodContainerLogsAsync(
                        runId,
                        namespaceName,
                        pod.Metadata.Name,
                        container.Name,
                        podServiceName,
                        cancellationToken));
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tail logs for runId={RunId}, namespace={Namespace}",
                runId, namespaceName);
        }
    }

    private async Task TailPodContainerLogsAsync(
        string runId,
        string namespaceName,
        string podName,
        string containerName,
        string? serviceName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Tailing logs for pod={Pod}, container={Container} in namespace={Namespace}",
                podName, containerName, namespaceName);

            // Watch logs from the pod
            var logStream = await _k8sClient.CoreV1.ReadNamespacedPodLogAsync(
                podName,
                namespaceName,
                container: containerName,
                follow: true,
                cancellationToken: cancellationToken);

            using var reader = new StreamReader(logStream);
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    var logEntry = new LogEntry
                    {
                        RunId = runId,
                        Source = "run",
                        ServiceName = serviceName,
                        Line = line,
                        Timestamp = DateTime.UtcNow
                    };

                    await _logRepository.AddLogAsync(logEntry, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to tail logs for pod={Pod}, container={Container}",
                podName, containerName);
        }
    }
}
