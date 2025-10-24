using Shared.Streams;
using RepoRunner.Contracts.Events;
using Google.Protobuf.WellKnownTypes;

namespace Runner.Services;

public class RunnerWorker : BackgroundService
{
    private readonly ILogger<RunnerWorker> _logger;
    private readonly IStreamConsumer<BuildSucceeded> _consumer;
    private readonly IStreamProducer<RunSucceeded> _runSucceededProducer;
    private readonly IStreamProducer<RunFailed> _runFailedProducer;
    private readonly IKubernetesResourceGenerator _resourceGenerator;
    private readonly IKubernetesDeployer _deployer;
    private readonly IRunRepository _runRepository;
    private readonly IPodLogTailer _podLogTailer;

    public RunnerWorker(
        ILogger<RunnerWorker> logger,
        IStreamConsumer<BuildSucceeded> consumer,
        IStreamProducer<RunSucceeded> runSucceededProducer,
        IStreamProducer<RunFailed> runFailedProducer,
        IKubernetesResourceGenerator resourceGenerator,
        IKubernetesDeployer deployer,
        IRunRepository runRepository,
        IPodLogTailer podLogTailer)
    {
        _logger = logger;
        _consumer = consumer;
        _runSucceededProducer = runSucceededProducer;
        _runFailedProducer = runFailedProducer;
        _resourceGenerator = resourceGenerator;
        _deployer = deployer;
        _runRepository = runRepository;
        _podLogTailer = podLogTailer;
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
                // Get run record
                var run = await _runRepository.GetByIdAsync(buildSucceeded.RunId, stoppingToken);
                if (run == null)
                {
                    _logger.LogWarning("Run not found: {RunId}", buildSucceeded.RunId);
                    return true; // Acknowledge anyway
                }

                // Generate Kubernetes resources based on mode
                KubernetesResources resources;
                string primaryServiceName = "app";

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
                    // Determine primary service from run record or use first service
                    primaryServiceName = buildSucceeded.Services
                        .FirstOrDefault(s => s.Name == run.ImageRefs.FirstOrDefault())?.Name
                        ?? buildSucceeded.Services.FirstOrDefault()?.Name
                        ?? "app";

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

                // Deploy to Kubernetes
                var namespaceName = await _deployer.DeployAsync(resources, stoppingToken);

                // Generate preview URL
                var previewUrl = _deployer.GetPreviewUrl(namespaceName, primaryServiceName, resources.ExposedPort);

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
}
