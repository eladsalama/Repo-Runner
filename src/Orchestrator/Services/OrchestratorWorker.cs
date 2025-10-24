using Shared.Streams;
using RepoRunner.Contracts.Events;

namespace Orchestrator.Services;

public class OrchestratorWorker : BackgroundService
{
    private readonly ILogger<OrchestratorWorker> _logger;
    private readonly IStreamConsumer<BuildSucceeded> _buildSucceededConsumer;
    private readonly IStreamConsumer<BuildFailed> _buildFailedConsumer;
    private readonly IRunRepository _runRepository;

    public OrchestratorWorker(
        ILogger<OrchestratorWorker> logger,
        IStreamConsumer<BuildSucceeded> buildSucceededConsumer,
        IStreamConsumer<BuildFailed> buildFailedConsumer,
        IRunRepository runRepository)
    {
        _logger = logger;
        _buildSucceededConsumer = buildSucceededConsumer;
        _buildFailedConsumer = buildFailedConsumer;
        _runRepository = runRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Orchestrator worker starting");
        
        // Start consuming BuildSucceeded events
        var buildSucceededTask = Task.Run(async () =>
        {
            await _buildSucceededConsumer.ConsumeAsync(async (buildSucceeded) =>
            {
                _logger.LogInformation(
                    "Received BuildSucceeded event: RunId={RunId}, Mode={Mode}",
                    buildSucceeded.RunId, buildSucceeded.Mode);

                try
                {
                    var run = await _runRepository.GetByIdAsync(buildSucceeded.RunId, stoppingToken);
                    if (run == null)
                    {
                        _logger.LogWarning("Run not found: {RunId}", buildSucceeded.RunId);
                        return true; // Acknowledge anyway
                    }

                    // Update run with build results
                    run.Status = "Building Complete - Ready for Deployment";
                    run.LogsRef = buildSucceeded.LogsRef;
                    
                    if (buildSucceeded.Mode == RunMode.Dockerfile)
                    {
                        run.ImageRef = buildSucceeded.ImageRef;
                        run.Ports = buildSucceeded.Ports.ToList();
                    }
                    else if (buildSucceeded.Mode == RunMode.Compose)
                    {
                        run.ImageRefs = buildSucceeded.Services.Select(s => s.ImageRef).ToList();
                        // Get ports from primary service or first service with ports
                        var primaryService = buildSucceeded.Services
                            .FirstOrDefault(s => s.Name == run.PrimaryService) 
                            ?? buildSucceeded.Services.FirstOrDefault();
                        if (primaryService != null)
                        {
                            run.Ports = primaryService.Ports.ToList();
                        }
                    }

                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} with build results", buildSucceeded.RunId);

                    // TODO: Produce RunSucceeded event for Runner to consume
                    // For now, we'll wait for Milestone 6 (Runner) implementation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process BuildSucceeded event for RunId={RunId}", buildSucceeded.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Start consuming BuildFailed events
        var buildFailedTask = Task.Run(async () =>
        {
            await _buildFailedConsumer.ConsumeAsync(async (buildFailed) =>
            {
                _logger.LogInformation(
                    "Received BuildFailed event: RunId={RunId}, Error={Error}",
                    buildFailed.RunId, buildFailed.Error);

                try
                {
                    var run = await _runRepository.GetByIdAsync(buildFailed.RunId, stoppingToken);
                    if (run == null)
                    {
                        _logger.LogWarning("Run not found: {RunId}", buildFailed.RunId);
                        return true; // Acknowledge anyway
                    }

                    // Update run with failure information
                    run.Status = "Failed";
                    run.ErrorMessage = buildFailed.Error;
                    run.LogsRef = buildFailed.LogsRef;
                    run.CompletedAt = buildFailed.FailedAt.ToDateTime();

                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} with build failure", buildFailed.RunId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process BuildFailed event for RunId={RunId}", buildFailed.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Wait for both consumers to complete (they run until cancellation)
        await Task.WhenAll(buildSucceededTask, buildFailedTask);
    }
}
