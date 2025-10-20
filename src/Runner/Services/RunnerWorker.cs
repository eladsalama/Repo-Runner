namespace Runner.Services;

public class RunnerWorker : BackgroundService
{
    private readonly ILogger<RunnerWorker> _logger;

    public RunnerWorker(ILogger<RunnerWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runner worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Runner worker running at: {time}", DateTimeOffset.Now);
            // TODO: Listen for BuildSucceeded events, deploy to K8s, expose preview URLs
            await Task.Delay(10000, stoppingToken);
        }
    }
}
