using k8s;
using k8s.Models;

namespace Runner.Services;

public class KubernetesDeployer : IKubernetesDeployer
{
    private readonly ILogger<KubernetesDeployer> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly IConfiguration _configuration;

    public KubernetesDeployer(
        ILogger<KubernetesDeployer> logger,
        IKubernetes kubernetes,
        IConfiguration configuration)
    {
        _logger = logger;
        _kubernetes = kubernetes;
        _configuration = configuration;
    }

    public async Task<string> DeployAsync(KubernetesResources resources, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deploying to namespace {Namespace}", resources.NamespaceName);

        try
        {
            // Create namespace
            await _kubernetes.CoreV1.CreateNamespaceAsync(resources.Namespace, cancellationToken: cancellationToken);
            _logger.LogInformation("Created namespace {Namespace}", resources.NamespaceName);

            // Create deployments
            foreach (var deployment in resources.Deployments)
            {
                await _kubernetes.AppsV1.CreateNamespacedDeploymentAsync(
                    deployment,
                    resources.NamespaceName,
                    cancellationToken: cancellationToken);
                _logger.LogInformation(
                    "Created deployment {Deployment} in namespace {Namespace}",
                    deployment.Metadata.Name, resources.NamespaceName);
            }

            // Create services
            foreach (var service in resources.Services)
            {
                await _kubernetes.CoreV1.CreateNamespacedServiceAsync(
                    service,
                    resources.NamespaceName,
                    cancellationToken: cancellationToken);
                _logger.LogInformation(
                    "Created service {Service} in namespace {Namespace}",
                    service.Metadata.Name, resources.NamespaceName);
            }

            // Wait for at least one pod to be ready
            await WaitForPodsReadyAsync(resources.NamespaceName, cancellationToken);

            _logger.LogInformation("Successfully deployed to namespace {Namespace}", resources.NamespaceName);
            return resources.NamespaceName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy to namespace {Namespace}", resources.NamespaceName);
            
            // Attempt cleanup on failure
            try
            {
                await DeleteNamespaceAsync(resources.NamespaceName, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup namespace {Namespace} after deployment failure", resources.NamespaceName);
            }
            
            throw;
        }
    }

    public async Task DeleteNamespaceAsync(string namespaceName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting namespace {Namespace}", namespaceName);

        try
        {
            await _kubernetes.CoreV1.DeleteNamespaceAsync(
                namespaceName,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted namespace {Namespace}", namespaceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete namespace {Namespace}", namespaceName);
            throw;
        }
    }

    public string GetPreviewUrl(string namespaceName, string serviceName, int port)
    {
        // For kind cluster, we use NodePort to expose services
        // In production, this would use Ingress or LoadBalancer
        var nodePort = _configuration.GetValue<int>("Runner:NodePort", 30080);
        return $"http://localhost:{nodePort}";
    }

    private async Task WaitForPodsReadyAsync(string namespaceName, CancellationToken cancellationToken)
    {
        var maxWaitSeconds = 60;
        var waited = 0;

        _logger.LogInformation("Waiting for pods in namespace {Namespace} to be ready", namespaceName);

        while (waited < maxWaitSeconds && !cancellationToken.IsCancellationRequested)
        {
            var pods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                cancellationToken: cancellationToken);

            if (pods.Items.Any() && pods.Items.All(p => 
                p.Status?.Phase == "Running" && 
                p.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true))
            {
                _logger.LogInformation("All pods in namespace {Namespace} are ready", namespaceName);
                return;
            }

            await Task.Delay(1000, cancellationToken);
            waited++;
        }

        if (waited >= maxWaitSeconds)
        {
            _logger.LogWarning(
                "Timeout waiting for pods in namespace {Namespace} to be ready after {Seconds}s",
                namespaceName, maxWaitSeconds);
        }
    }
}
