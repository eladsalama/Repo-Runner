using Shared.Streams;
using RepoRunner.Contracts.Events;

namespace Indexer.Services;

public class IndexerWorker : BackgroundService
{
    private readonly ILogger<IndexerWorker> _logger;
    private readonly IStreamConsumer<RunRequested> _consumer;

    public IndexerWorker(
        ILogger<IndexerWorker> logger,
        IStreamConsumer<RunRequested> consumer)
    {
        _logger = logger;
        _consumer = consumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Indexer worker starting");
        
        // Start consuming RunRequested events
        await _consumer.ConsumeAsync(async (runRequested) =>
        {
            _logger.LogInformation(
                "Received RunRequested event for indexing: RunId={RunId}, RepoUrl={RepoUrl}",
                runRequested.RunId, runRequested.RepoUrl);
            
            // TODO: Index README/Dockerfile for RAG
            // For now, just log and acknowledge
            await Task.Delay(100, stoppingToken);
            
            return true; // Acknowledge
        }, stoppingToken);
    }
}
