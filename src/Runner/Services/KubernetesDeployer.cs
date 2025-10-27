using k8s;
using k8s.Models;

namespace Runner.Services;

public class KubernetesDeployer : IKubernetesDeployer
{
    private readonly ILogger<KubernetesDeployer> _logger;
    private readonly IKubernetes _kubernetes;
    private readonly IConfiguration _configuration;
    private readonly PortForwardManager _portForwardManager;

    public KubernetesDeployer(
        ILogger<KubernetesDeployer> logger,
        IKubernetes kubernetes,
        IConfiguration configuration,
        PortForwardManager portForwardManager)
    {
        _logger = logger;
        _kubernetes = kubernetes;
        _configuration = configuration;
        _portForwardManager = portForwardManager;
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

            // Run post-deployment initialization (e.g., Prisma migrations for Node.js apps)
            await RunPostDeploymentInitAsync(resources.NamespaceName, resources.Deployments, cancellationToken);

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
            // Clean up port-forwards first
            _portForwardManager.CleanupNamespace(namespaceName);
            
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

    public async Task<List<string>> ListNamespacesAsync(string prefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var namespaces = await _kubernetes.CoreV1.ListNamespaceAsync(cancellationToken: cancellationToken);
            return namespaces.Items
                .Where(ns => ns.Metadata.Name.StartsWith(prefix))
                .Select(ns => ns.Metadata.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list namespaces with prefix {Prefix}", prefix);
            return new List<string>();
        }
    }

    public string GetPreviewUrl(string namespaceName, string serviceName, int port)
    {
        // For kind cluster, we need port-forwarding instead of NodePort
        // Create port-forward automatically
        try
        {
            var url = _portForwardManager.CreatePortForwardAsync(
                namespaceName,
                serviceName,
                port).GetAwaiter().GetResult();
            
            _logger.LogInformation(
                "Created port-forward for {Service}: {Url}",
                serviceName, url);
            
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create port-forward for {Service} in {Namespace}",
                serviceName, namespaceName);
            
            // Fallback to NodePort (won't work in kind but better than nothing)
            var nodePort = _configuration.GetValue<int>("Runner:NodePort", 30080);
            return $"http://localhost:{nodePort}";
        }
    }

    public Dictionary<string, string> GetAllServiceUrls(KubernetesResources resources)
    {
        // Create port-forwards for all services (use first port from each service)
        try
        {
            var servicePorts = resources.ServicePorts
                .Where(kvp => kvp.Value.Any())
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.First());
            
            var urls = _portForwardManager.CreatePortForwardsForNamespaceAsync(
                resources.NamespaceName,
                servicePorts).GetAwaiter().GetResult();
            
            _logger.LogInformation(
                "Created {Count} port-forward(s) for namespace {Namespace}",
                urls.Count, resources.NamespaceName);
            
            return urls;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create port-forwards for namespace {Namespace}",
                resources.NamespaceName);
            return new Dictionary<string, string>();
        }
    }

    private async Task WaitForPodsReadyAsync(string namespaceName, CancellationToken cancellationToken)
    {
        var maxWaitSeconds = 45; // Short timeout - give up on crash looping pods quickly
        var minWaitForAtLeastOne = 20; // Wait at least 20s for first pod
        var waited = 0;
        var crashLoopingPods = new HashSet<string>();
        var failedImagePullPods = new HashSet<string>();

        _logger.LogInformation("Waiting for pods in namespace {Namespace} to be ready (max {MaxSeconds}s)", namespaceName, maxWaitSeconds);

        while (waited < maxWaitSeconds && !cancellationToken.IsCancellationRequested)
        {
            var pods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                cancellationToken: cancellationToken);

            if (pods.Items.Any())
            {
                var totalPods = pods.Items.Count;
                var readyPods = pods.Items.Count(p => 
                    p.Status?.Phase == "Running" && 
                    p.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true);

                // Track crash looping and image pull failures
                foreach (var pod in pods.Items)
                {
                    var podName = pod.Metadata.Name;
                    var containerStatuses = pod.Status?.ContainerStatuses ?? new List<V1ContainerStatus>();
                    
                    foreach (var container in containerStatuses)
                    {
                        if (!container.Ready)
                        {
                            var waiting = container.State?.Waiting;
                            var terminated = container.State?.Terminated;
                            
                            if (waiting != null)
                            {
                                // Mark pods that are stuck in CrashLoopBackOff
                                if (waiting.Reason == "CrashLoopBackOff" && !crashLoopingPods.Contains(podName))
                                {
                                    crashLoopingPods.Add(podName);
                                    _logger.LogWarning(
                                        "⚠️ Pod {Pod} is crash looping - will continue with other pods",
                                        podName);
                                }
                                // Mark pods that can't pull images
                                else if ((waiting.Reason == "ImagePullBackOff" || waiting.Reason == "ErrImagePull") && !failedImagePullPods.Contains(podName))
                                {
                                    failedImagePullPods.Add(podName);
                                    _logger.LogWarning(
                                        "⚠️ Pod {Pod} image pull failed - will continue with other pods",
                                        podName);
                                }
                            }
                            else if (terminated != null)
                            {
                                _logger.LogDebug(
                                    "Pod {Pod} container {Container} terminated: {Reason} (exit {ExitCode})",
                                    podName, container.Name, terminated.Reason, terminated.ExitCode);
                            }
                        }
                    }
                }

                var degradedPods = crashLoopingPods.Count + failedImagePullPods.Count;
                var healthyOrStartingPods = totalPods - degradedPods;

                _logger.LogInformation(
                    "Pod readiness: {Ready}/{Healthy} healthy ({Degraded} degraded) in namespace {Namespace} ({Waited}s elapsed)",
                    readyPods, healthyOrStartingPods, degradedPods, namespaceName, waited);

                // Succeed if:
                // 1. All pods are ready, OR
                // 2. At least one pod is ready AND we've waited at least 30s (give up on crash loopers)
                if (readyPods == totalPods)
                {
                    _logger.LogInformation("✅ All {Total} pod(s) in namespace {Namespace} are ready", totalPods, namespaceName);
                    return;
                }
                else if (readyPods > 0 && waited >= minWaitForAtLeastOne)
                {
                    _logger.LogWarning(
                        "⚠️ Proceeding with {Ready}/{Total} pods ready. Degraded pods: {Degraded}",
                        readyPods, totalPods, string.Join(", ", crashLoopingPods.Concat(failedImagePullPods)));
                    return; // Proceed with partial success
                }

                // Check if any pods are in permanent error state
                var errorPods = pods.Items.Where(p => p.Status?.Phase == "Failed" || p.Status?.Phase == "Error").ToList();
                if (errorPods.Any())
                {
                    var errorMessages = errorPods.Select(p => $"{p.Metadata.Name}: {p.Status?.Reason}");
                    _logger.LogError("Pods in error state: {Errors}", string.Join(", ", errorMessages));
                    // Don't throw - treat as degraded
                    foreach (var ep in errorPods)
                    {
                        crashLoopingPods.Add(ep.Metadata.Name);
                    }
                }
            }

            await Task.Delay(3000, cancellationToken);
            waited += 3;
        }

        // After timeout, check if we have at least one pod ready
        var finalPods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
            namespaceName,
            cancellationToken: cancellationToken);
        
        var finalReadyCount = finalPods.Items.Count(p => 
            p.Status?.Phase == "Running" && 
            p.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true);

        if (finalReadyCount > 0)
        {
            _logger.LogWarning(
                "⏱️ Timeout reached but {Ready}/{Total} pods are ready - proceeding with partial deployment",
                finalReadyCount, finalPods.Items.Count);
            return; // Partial success
        }

        // Complete failure - no pods ready
        var podStatuses = finalPods.Items.Select(p => 
            $"{p.Metadata.Name}: {p.Status?.Phase}");
        
        _logger.LogError(
            "❌ No pods became ready in namespace {Namespace} after {Seconds}s. Pod statuses: {Statuses}",
            namespaceName, maxWaitSeconds, string.Join("; ", podStatuses));
        
        
        throw new TimeoutException($"No pods in namespace {namespaceName} became ready within {maxWaitSeconds} seconds");
    }

    private async Task RunPostDeploymentInitAsync(
        string namespaceName, 
        List<V1Deployment> deployments,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running post-deployment initialization for namespace {Namespace}", namespaceName);

        foreach (var deployment in deployments)
        {
            var serviceName = deployment.Metadata.Name;
            
            // Check if this is a Node.js app with Prisma (api service typically)
            if (serviceName.Contains("api", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await RunPrismaMigrationsAsync(namespaceName, serviceName, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "Failed to run Prisma migrations for {Service}, app may not work correctly",
                        serviceName);
                    // Don't fail the deployment, just log the warning
                }
            }
        }
    }

    private async Task RunPrismaMigrationsAsync(
        string namespaceName,
        string serviceName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to run Prisma migrations for {Service}", serviceName);

        try
        {
            // Get the first pod for this service
            var pods = await _kubernetes.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: $"app={serviceName}",
                cancellationToken: cancellationToken);

            var pod = pods.Items.FirstOrDefault(p => 
                p.Status?.Phase == "Running" && 
                p.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true);

            if (pod == null)
            {
                _logger.LogWarning("No ready pod found for service {Service}, skipping migrations", serviceName);
                return;
            }

            var podName = pod.Metadata.Name;
            _logger.LogInformation("Running Prisma migrations in pod {Pod}", podName);

            // Execute: npx prisma migrate deploy
            var execResult = await ExecuteCommandInPodAsync(
                namespaceName,
                podName,
                new[] { "npx", "prisma", "migrate", "deploy" },
                cancellationToken);

            if (execResult.Contains("migration", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("✅ Prisma migrations completed successfully for {Service}", serviceName);
            }
            else
            {
                _logger.LogInformation("Prisma migrations result: {Result}", execResult);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not run Prisma migrations for {Service}", serviceName);
            throw;
        }
    }

    private async Task<string> ExecuteCommandInPodAsync(
        string namespaceName,
        string podName,
        string[] command,
        CancellationToken cancellationToken)
    {
        var kubeConfig = _configuration.GetValue<string>("KUBECONFIG") 
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

        var args = $"exec -n {namespaceName} {podName} -- {string.Join(" ", command)}";
        
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "kubectl",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        startInfo.EnvironmentVariables["KUBECONFIG"] = kubeConfig;

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start kubectl process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}: {error}");
        }

        return output + error;
    }
}

