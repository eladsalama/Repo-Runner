using Shared.Streams;
using RepoRunner.Contracts.Events;

namespace Builder.Services;

public class BuilderWorker : BackgroundService
{
    private readonly ILogger<BuilderWorker> _logger;
    private readonly IStreamConsumer<RunRequested> _consumer;

    public BuilderWorker(
        ILogger<BuilderWorker> logger,
        IStreamConsumer<RunRequested> consumer)
    {
        _logger = logger;
        _consumer = consumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Builder worker starting");
        
        // Start consuming RunRequested events
        await _consumer.ConsumeAsync(async (runRequested) =>
        {
            _logger.LogInformation(
                "Received RunRequested event: RunId={RunId}, RepoUrl={RepoUrl}",
                runRequested.RunId, runRequested.RepoUrl);
            
            // TODO: Clone repo, build Docker image
            // For now, just log and acknowledge
            await Task.Delay(100, stoppingToken);
            
            return true; // Acknowledge
        }, stoppingToken);
    }
}
