using Shared.Streams;
using RepoRunner.Contracts.Events;

namespace Runner.Services;

public class RunnerWorker : BackgroundService
{
    private readonly ILogger<RunnerWorker> _logger;
    private readonly IStreamConsumer<BuildSucceeded> _consumer;

    public RunnerWorker(
        ILogger<RunnerWorker> logger,
        IStreamConsumer<BuildSucceeded> consumer)
    {
        _logger = logger;
        _consumer = consumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runner worker starting");
        
        // Start consuming BuildSucceeded events
        await _consumer.ConsumeAsync(async (buildSucceeded) =>
        {
            _logger.LogInformation(
                "Received BuildSucceeded event: RunId={RunId}, ImageRef={ImageRef}",
                buildSucceeded.RunId, buildSucceeded.ImageRef);
            
            // TODO: Deploy to K8s, expose preview URLs
            // For now, just log and acknowledge
            await Task.Delay(100, stoppingToken);
            
            return true; // Acknowledge
        }, stoppingToken);
    }
}
