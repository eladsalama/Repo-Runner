using Shared.Streams;
using Shared.Repositories;
using RepoRunner.Contracts.Events;
using Google.Protobuf.WellKnownTypes;

namespace Runner.Services;

public class RunnerWorker : BackgroundService
{
    private readonly ILogger<RunnerWorker> _logger;
    private readonly IStreamConsumer<BuildSucceeded> _consumer;
    private readonly IStreamProducer<RunSucceeded> _runSucceededProducer;
    private readonly IStreamProducer<RunFailed> _runFailedProducer;
    private readonly IStreamProducer<BuildProgress> _buildProgressProducer;
    private readonly IKubernetesResourceGenerator _resourceGenerator;
    private readonly IKubernetesDeployer _deployer;
    private readonly IRunRepository _runRepository;
    private readonly IPodLogTailer _podLogTailer;
    private readonly PortForwardManager _portForwardManager;

    public RunnerWorker(
        ILogger<RunnerWorker> logger,
        IStreamConsumer<BuildSucceeded> consumer,
        IStreamProducer<RunSucceeded> runSucceededProducer,
        IStreamProducer<RunFailed> runFailedProducer,
        IStreamProducer<BuildProgress> buildProgressProducer,
        IKubernetesResourceGenerator resourceGenerator,
        IKubernetesDeployer deployer,
        IRunRepository runRepository,
        IPodLogTailer podLogTailer,
        PortForwardManager portForwardManager)
    {
        _logger = logger;
        _consumer = consumer;
        _runSucceededProducer = runSucceededProducer;
        _runFailedProducer = runFailedProducer;
        _buildProgressProducer = buildProgressProducer;
        _resourceGenerator = resourceGenerator;
        _deployer = deployer;
        _runRepository = runRepository;
        _podLogTailer = podLogTailer;
        _portForwardManager = portForwardManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runner worker starting");
        
        // Start consuming BuildSucceeded events
        await _consumer.ConsumeAsync(async (buildSucceeded) =>
        {
            _logger.LogInformation(
                "Received BuildSucceeded event: RunId={RunId}, Mode={Mode}",
                buildSucceeded.RunId, buildSucceeded.Mode);
            
            try
            {
                // CLEANUP OLD NAMESPACES FIRST (only 1 run at a time)
                try
                {
                    _logger.LogInformation("Cleaning up old run namespaces before starting new run");
                    var oldNamespaces = await _deployer.ListNamespacesAsync("run-", stoppingToken);
                    foreach (var ns in oldNamespaces)
                    {
                        _logger.LogInformation("Deleting old namespace: {Namespace}", ns);
                        await _deployer.DeleteNamespaceAsync(ns, stoppingToken);
                    }

                    // Also cleanup ALL port-forwards (only 1 run at a time, so all old port-forwards must go)
                    _logger.LogInformation("Cleaning up all old port-forwards");
                    await _portForwardManager.CleanupAllPortForwardsAsync();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup old namespaces/port-forwards, continuing anyway");
                }

                // Get run record
                Run? run = null;
                try
                {
                    run = await _runRepository.GetByIdAsync(buildSucceeded.RunId, stoppingToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, 
                        "Failed to retrieve run {RunId} from database - data may be corrupted or schema mismatch. Skipping this run.",
                        buildSucceeded.RunId);
                    return true; // ACK to skip this corrupted message
                }
                
                if (run == null)
                {
                    _logger.LogWarning("Run not found: {RunId} - will retry (race condition with Orchestrator)", buildSucceeded.RunId);
                    return false; // Retry - Run record may not be created yet by Orchestrator
                }

                // Generate Kubernetes resources based on mode
                KubernetesResources resources;
                string primaryServiceName = "app";

                // Emit progress: Generating Kubernetes resources
                await EmitDeploymentProgressAsync(buildSucceeded.RunId, "Generating Kubernetes resources", stoppingToken);

                if (buildSucceeded.Mode == RunMode.Dockerfile)
                {
                    resources = await _resourceGenerator.GenerateDockerfileModeResourcesAsync(
                        buildSucceeded.RunId,
                        run.RepoUrl,
                        buildSucceeded.ImageRef,
                        buildSucceeded.Ports.ToList(),
                        stoppingToken);
                }
                else if (buildSucceeded.Mode == RunMode.Compose)
                {
                    // Use primary service from run record, fallback to first service with web-like ports
                    primaryServiceName = !string.IsNullOrEmpty(run.PrimaryService) 
                        ? run.PrimaryService
                        : buildSucceeded.Services.FirstOrDefault(s => s.Ports.Any(p => p == 80 || p == 3000 || p == 8080 || p == 3100 || p == 5000))?.Name
                        ?? buildSucceeded.Services.FirstOrDefault()?.Name
                        ?? "app";

                    _logger.LogInformation(
                        "Selected primary service: {PrimaryService} for RunId={RunId}",
                        primaryServiceName, buildSucceeded.RunId);

                    resources = await _resourceGenerator.GenerateComposeModeResourcesAsync(
                        buildSucceeded.RunId,
                        run.RepoUrl,
                        primaryServiceName,
                        buildSucceeded.Services.ToList(),
                        stoppingToken);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown run mode: {buildSucceeded.Mode}");
                }

                // Emit progress: Deploying to Kubernetes
                await EmitDeploymentProgressAsync(buildSucceeded.RunId, "Deploying to Kubernetes", stoppingToken);

                // Deploy to Kubernetes
                var namespaceName = await _deployer.DeployAsync(resources, stoppingToken);

                // Emit progress: Waiting for application to be ready
                await EmitDeploymentProgressAsync(buildSucceeded.RunId, "Application is starting", stoppingToken);

                // Generate preview URL (primary service)
                var previewUrl = _deployer.GetPreviewUrl(namespaceName, primaryServiceName, resources.ExposedPort);

                // Log all accessible service URLs
                var allServiceUrls = _deployer.GetAllServiceUrls(resources);
                if (allServiceUrls.Any())
                {
                    _logger.LogInformation("📍 All accessible service endpoints:");
                    foreach (var kvp in allServiceUrls)
                    {
                        _logger.LogInformation("  • {Service} → {Url}", kvp.Key, kvp.Value);
                    }
                }

                // Update run record
                run.Status = "Running";
                run.NamespaceName = namespaceName;
                run.PreviewUrl = previewUrl;
                run.StartedAt = DateTime.UtcNow;
                await _runRepository.UpdateAsync(run, stoppingToken);

                // Produce RunSucceeded event
                var runSucceeded = new RunSucceeded
                {
                    RunId = buildSucceeded.RunId,
                    PreviewUrl = previewUrl,
                    Namespace = namespaceName,
                    StartedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _runSucceededProducer.PublishAsync(runSucceeded, stoppingToken);

                _logger.LogInformation(
                    "Successfully deployed RunId={RunId} to namespace={Namespace}, PreviewURL={PreviewUrl}",
                    buildSucceeded.RunId, namespaceName, previewUrl);

                // Start tailing logs in background (don't await)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _podLogTailer.TailLogsAsync(buildSucceeded.RunId, namespaceName, null, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error tailing logs for RunId={RunId}", buildSucceeded.RunId);
                    }
                }, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy RunId={RunId}: {Error}", buildSucceeded.RunId, ex.Message);

                try
                {
                    // Update run record
                    var run = await _runRepository.GetByIdAsync(buildSucceeded.RunId, stoppingToken);
                    if (run != null)
                    {
                        run.Status = "Failed";
                        run.ErrorMessage = ex.Message;
                        run.CompletedAt = DateTime.UtcNow;
                        await _runRepository.UpdateAsync(run, stoppingToken);
                    }

                    // Produce RunFailed event
                    var runFailed = new RunFailed
                    {
                        RunId = buildSucceeded.RunId,
                        Error = ex.Message,
                        FailedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                    };
                    await _runFailedProducer.PublishAsync(runFailed, stoppingToken);
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to handle deployment failure for RunId={RunId}", buildSucceeded.RunId);
                }
            }
            
            return true; // Acknowledge
        }, stoppingToken);
    }

    /// <summary>
    /// Emit deployment progress updates for better UX
    /// </summary>
    private async Task EmitDeploymentProgressAsync(string runId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var progress = new BuildProgress
            {
                RunId = runId,
                Current = 0,
                Total = 0,
                ServiceName = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await _buildProgressProducer.PublishAsync(progress, cancellationToken);
            _logger.LogInformation("📊 Deployment: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit deployment progress for RunId={RunId}", runId);
        }
    }
}
