using Shared.Streams;
using Shared.Cache;
using Shared.Repositories;
using RepoRunner.Contracts.Events;

namespace Orchestrator.Services;

public class OrchestratorWorker : BackgroundService
{
    private readonly ILogger<OrchestratorWorker> _logger;
    private readonly IStreamConsumer<RunRequested> _runRequestedConsumer;
    private readonly IStreamConsumer<BuildSucceeded> _buildSucceededConsumer;
    private readonly IStreamConsumer<BuildFailed> _buildFailedConsumer;
    private readonly IStreamConsumer<BuildProgress> _buildProgressConsumer;
    private readonly IStreamConsumer<RunSucceeded> _runSucceededConsumer;
    private readonly IStreamConsumer<RunFailed> _runFailedConsumer;
    private readonly IRunRepository _runRepository;
    private readonly IRunStatusCache _statusCache;

    public OrchestratorWorker(
        ILogger<OrchestratorWorker> logger,
        IStreamConsumer<RunRequested> runRequestedConsumer,
        IStreamConsumer<BuildSucceeded> buildSucceededConsumer,
        IStreamConsumer<BuildFailed> buildFailedConsumer,
        IStreamConsumer<BuildProgress> buildProgressConsumer,
        IStreamConsumer<RunSucceeded> runSucceededConsumer,
        IStreamConsumer<RunFailed> runFailedConsumer,
        IRunRepository runRepository,
        IRunStatusCache statusCache)
    {
        _logger = logger;
        _runRequestedConsumer = runRequestedConsumer;
        _buildSucceededConsumer = buildSucceededConsumer;
        _buildFailedConsumer = buildFailedConsumer;
        _buildProgressConsumer = buildProgressConsumer;
        _runSucceededConsumer = runSucceededConsumer;
        _runFailedConsumer = runFailedConsumer;
        _runRepository = runRepository;
        _statusCache = statusCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Orchestrator worker starting");
        
        // Start consuming RunRequested events
        var runRequestedTask = Task.Run(async () =>
        {
            await _runRequestedConsumer.ConsumeAsync(async (runRequested) =>
            {
                _logger.LogInformation(
                    "Received RunRequested event: RunId={RunId}, RepoUrl={RepoUrl}, Mode={Mode}",
                    runRequested.RunId, runRequested.RepoUrl, runRequested.Mode);

                try
                {
                    // Create run record in MongoDB
                    var run = new Models.Run
                    {
                        Id = runRequested.RunId,
                        RepoUrl = runRequested.RepoUrl,
                        Branch = runRequested.Branch,
                        Mode = runRequested.Mode,
                        ComposePath = runRequested.ComposePath,
                        PrimaryService = runRequested.PrimaryService,
                        Status = "Building",  // Start with Building since Builder will start immediately
                        CreatedAt = DateTime.UtcNow
                    };

                    await _runRepository.CreateAsync(run, stoppingToken);
                    _logger.LogInformation("Created Run record in MongoDB: {RunId}", runRequested.RunId);

                    // Update cache with Building status
                    await _statusCache.SetAsync(runRequested.RunId, new Shared.Cache.RunStatusCacheEntry
                    {
                        RunId = runRequested.RunId,
                        Status = "Building",
                        Mode = runRequested.Mode.ToString(),
                        PrimaryService = runRequested.PrimaryService
                    }, stoppingToken);

                    _logger.LogInformation("Run {RunId} is now Queued and ready for Builder", runRequested.RunId);

                    return true; // Acknowledge
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing RunRequested: {RunId}", runRequested.RunId);
                    return false; // Will retry
                }
            }, stoppingToken);
        }, stoppingToken);
        
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
                        _logger.LogWarning("Run not found: {RunId} - will retry (race condition with RunRequested)", buildSucceeded.RunId);
                        return false; // Retry - Run record may not be created yet
                    }

                    // Update run with build results (keep status as "Building" until deployment completes)
                    run.Status = "Building";  // Build complete, deployment in progress
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

                    // Update cache
                    await UpdateCacheAsync(run, stoppingToken);
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
                        _logger.LogWarning("Run not found: {RunId} - will retry (race condition with RunRequested)", buildFailed.RunId);
                        return false; // Retry - Run record may not be created yet
                    }

                    // Update run with failure information
                    run.Status = "Failed";
                    run.ErrorMessage = buildFailed.Error;
                    run.LogsRef = buildFailed.LogsRef;
                    run.CompletedAt = buildFailed.FailedAt.ToDateTime();

                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} with build failure", buildFailed.RunId);

                    // Update cache
                    await UpdateCacheAsync(run, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process BuildFailed event for RunId={RunId}", buildFailed.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Start consuming RunSucceeded events
        var runSucceededTask = Task.Run(async () =>
        {
            await _runSucceededConsumer.ConsumeAsync(async (runSucceeded) =>
            {
                _logger.LogInformation(
                    "Received RunSucceeded event: RunId={RunId}, PreviewUrl={PreviewUrl}",
                    runSucceeded.RunId, runSucceeded.PreviewUrl);

                try
                {
                    var run = await _runRepository.GetByIdAsync(runSucceeded.RunId, stoppingToken);
                    if (run == null)
                    {
                        _logger.LogWarning("Run not found: {RunId} - will retry (race condition with RunRequested)", runSucceeded.RunId);
                        return false; // Retry - Run record may not be created yet
                    }

                    // Update run with success information
                    run.Status = "Succeeded";
                    run.PreviewUrl = runSucceeded.PreviewUrl;
                    run.NamespaceName = runSucceeded.Namespace;
                    run.CompletedAt = DateTime.UtcNow;

                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} to Succeeded", runSucceeded.RunId);

                    // Update cache
                    await UpdateCacheAsync(run, stoppingToken);
                    _logger.LogInformation("Updated cache for run {RunId}", runSucceeded.RunId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process RunSucceeded event for RunId={RunId}", runSucceeded.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Start consuming RunFailed events
        var runFailedTask = Task.Run(async () =>
        {
            await _runFailedConsumer.ConsumeAsync(async (runFailed) =>
            {
                _logger.LogInformation(
                    "Received RunFailed event: RunId={RunId}, Error={Error}",
                    runFailed.RunId, runFailed.Error);

                try
                {
                    var run = await _runRepository.GetByIdAsync(runFailed.RunId, stoppingToken);
                    if (run == null)
                    {
                        _logger.LogWarning("Run not found: {RunId} - will retry (race condition with RunRequested)", runFailed.RunId);
                        return false; // Retry - Run record may not be created yet
                    }

                    // Update run with failure information
                    run.Status = "Failed";
                    run.ErrorMessage = runFailed.Error;
                    run.CompletedAt = runFailed.FailedAt.ToDateTime();

                    await _runRepository.UpdateAsync(run, stoppingToken);
                    _logger.LogInformation("Updated run {RunId} to Failed", runFailed.RunId);

                    // Update cache
                    await UpdateCacheAsync(run, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process RunFailed event for RunId={RunId}", runFailed.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Start consuming BuildProgress events
        var buildProgressTask = Task.Run(async () =>
        {
            await _buildProgressConsumer.ConsumeAsync(async (buildProgress) =>
            {
                _logger.LogInformation(
                    "Received BuildProgress event: RunId={RunId}, Progress=[{Current}/{Total}] {ServiceName}",
                    buildProgress.RunId, buildProgress.Current, buildProgress.Total, buildProgress.ServiceName);

                try
                {
                    // Update cache directly with progress
                    var existingCache = await _statusCache.GetAsync(buildProgress.RunId, stoppingToken);
                    if (existingCache != null)
                    {
                        // Format progress message nicely
                        string progressMessage;
                        if (buildProgress.Current > 0 && buildProgress.Total > 0)
                        {
                            progressMessage = $"[{buildProgress.Current}/{buildProgress.Total}] {buildProgress.ServiceName}";
                        }
                        else
                        {
                            // Generic progress message (e.g., "Loading images", "Deploying")
                            progressMessage = buildProgress.ServiceName;
                        }
                        
                        existingCache.BuildProgress = progressMessage;
                        await _statusCache.SetAsync(buildProgress.RunId, existingCache, stoppingToken);
                        _logger.LogDebug("Updated cache with build progress for RunId={RunId}: {Progress}", buildProgress.RunId, progressMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process BuildProgress event for RunId={RunId}", buildProgress.RunId);
                }

                return true; // Acknowledge
            }, stoppingToken);
        }, stoppingToken);

        // Wait for all consumers to complete (they run until cancellation)
        await Task.WhenAll(runRequestedTask, buildSucceededTask, buildFailedTask, buildProgressTask, runSucceededTask, runFailedTask);
    }

    private async Task UpdateCacheAsync(Models.Run run, CancellationToken cancellationToken)
    {
        try
        {
            var cacheEntry = new RunStatusCacheEntry
            {
                RunId = run.Id,
                Status = run.Status,
                PreviewUrl = run.PreviewUrl,
                StartedAt = run.StartedAt,
                EndedAt = run.CompletedAt,
                ErrorMessage = run.ErrorMessage,
                Mode = run.Mode.ToString(),
                PrimaryService = run.PrimaryService
            };
            await _statusCache.SetAsync(run.Id, cacheEntry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update cache for RunId={RunId}", run.Id);
        }
    }
}
