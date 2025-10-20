namespace Orchestrator.Services;

public class OrchestratorWorker : BackgroundService
{
    private readonly ILogger<OrchestratorWorker> _logger;

    public OrchestratorWorker(ILogger<OrchestratorWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Orchestrator worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Orchestrator worker running at: {time}", DateTimeOffset.Now);
            // TODO: Consume from Redis Streams, coordinate build/run lifecycle
            await Task.Delay(10000, stoppingToken);
        }
    }
}
