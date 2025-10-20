namespace Indexer.Services;

public class IndexerWorker : BackgroundService
{
    private readonly ILogger<IndexerWorker> _logger;

    public IndexerWorker(ILogger<IndexerWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Indexer worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Indexer worker running at: {time}", DateTimeOffset.Now);
            // TODO: Listen for RunRequested events, index README/Dockerfile for RAG
            await Task.Delay(10000, stoppingToken);
        }
    }
}
