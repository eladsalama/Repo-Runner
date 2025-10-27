using Shared.Streams;
using Shared.Repositories;
using RepoRunner.Contracts.Events;
using Runner.Services;

namespace Runner.Services;

/// <summary>
/// Background service that consumes RunStopRequested events and deletes namespaces
/// </summary>
public class StopRunnerWorker : BackgroundService
{
    private readonly ILogger<StopRunnerWorker> _logger;
    private readonly IStreamConsumer<RunStopRequested> _stopConsumer;
    private readonly IKubernetesDeployer _deployer;
    private readonly IRunRepository _runRepository;

    public StopRunnerWorker(
        ILogger<StopRunnerWorker> logger,
        IStreamConsumer<RunStopRequested> stopConsumer,
        IKubernetesDeployer deployer,
        IRunRepository runRepository)
    {
        _logger = logger;
        _stopConsumer = stopConsumer;
        _deployer = deployer;
        _runRepository = runRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StopRunner worker starting");

        await _stopConsumer.ConsumeAsync(async (stopRequest) =>
        {
            _logger.LogInformation(
                "Received RunStopRequested event: RunId={RunId}, Namespace={Namespace}",
                stopRequest.RunId, stopRequest.Namespace);

            try
            {
                // Delete the namespace
                await _deployer.DeleteNamespaceAsync(stopRequest.Namespace, stoppingToken);
                _logger.LogInformation("Deleted namespace {Namespace} for runId {RunId}",
                    stopRequest.Namespace, stopRequest.RunId);

                // Update run status in MongoDB
                var run = await _runRepository.GetByIdAsync(stopRequest.RunId, stoppingToken);
                if (run != null)
                {
                    run.Status = "Stopped";
                    run.CompletedAt = DateTime.UtcNow;
                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} status to Stopped", stopRequest.RunId);
                }

                return true; // Acknowledge
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process RunStopRequested event for RunId={RunId}",
                    stopRequest.RunId);
                // Still acknowledge to avoid retrying - namespace deletion is idempotent
                return true;
            }
        }, stoppingToken);
    }
}
