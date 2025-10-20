namespace Builder.Services;

public class BuilderWorker : BackgroundService
{
    private readonly ILogger<BuilderWorker> _logger;

    public BuilderWorker(ILogger<BuilderWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Builder worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Builder worker running at: {time}", DateTimeOffset.Now);
            // TODO: Listen for RunRequested events, clone repo, build Docker image
            await Task.Delay(10000, stoppingToken);
        }
    }
}
