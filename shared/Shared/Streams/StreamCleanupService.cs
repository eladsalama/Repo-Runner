using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Streams;

/// <summary>
/// Service that cleans up Redis streams on application startup AND shutdown.
/// On STARTUP: Optionally flushes all streams to ensure clean state (prevents corrupted message retries).
/// On SHUTDOWN: Cleans up streams to prevent corruption persisting across restarts.
/// </summary>
public class StreamCleanupService : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StreamCleanupService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly bool _flushOnStartup;

    public StreamCleanupService(
        IConnectionMultiplexer redis,
        ILogger<StreamCleanupService> logger,
        IHostApplicationLifetime lifetime,
        bool flushOnStartup = false)
    {
        _redis = redis;
        _logger = logger;
        _lifetime = lifetime;
        _flushOnStartup = flushOnStartup;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // FLUSH STREAMS ON STARTUP - ensures clean state every time
        if (_flushOnStartup)
        {
            await FlushStreamsAsync();
        }
        
        // Register cleanup on shutdown
        _lifetime.ApplicationStopping.Register(OnStopping);
        _logger.LogInformation("Stream cleanup service initialized (flushOnStartup={FlushOnStartup})", _flushOnStartup);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void OnStopping()
    {
        try
        {
            // Synchronous version for shutdown callback
            var db = _redis.GetDatabase();
            db.KeyDelete(StreamConfig.Streams.RepoRuns);
            db.KeyDelete(StreamConfig.Streams.Indexing);
            db.KeyDelete(StreamConfig.DeadLetterQueue);
            
            _logger.LogInformation("Cleaned up Redis streams on shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup Redis streams on shutdown");
        }
    }

    private async Task FlushStreamsAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            
            _logger.LogInformation("FLUSHING Redis streams on startup to ensure clean state...");
            
            // Delete all streams and their consumer groups
            var deletedRepoRuns = await db.KeyDeleteAsync(StreamConfig.Streams.RepoRuns);
            var deletedIndexing = await db.KeyDeleteAsync(StreamConfig.Streams.Indexing);
            var deletedDlq = await db.KeyDeleteAsync(StreamConfig.DeadLetterQueue);
            
            _logger.LogInformation(
                "Flushed Redis streams: repo-runs={RepoRuns}, indexing={Indexing}, dlq={Dlq}",
                deletedRepoRuns, deletedIndexing, deletedDlq);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush Redis streams on startup - continuing anyway");
        }
    }
}
