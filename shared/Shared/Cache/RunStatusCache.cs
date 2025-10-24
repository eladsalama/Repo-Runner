using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Shared.Cache;

/// <summary>
/// Cache service for run status with Redis
/// </summary>
public interface IRunStatusCache
{
    Task SetAsync(string runId, RunStatusCacheEntry status, CancellationToken cancellationToken = default);
    Task<RunStatusCacheEntry?> GetAsync(string runId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string runId, CancellationToken cancellationToken = default);
}

public class RunStatusCache : IRunStatusCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RunStatusCache> _logger;
    private readonly TimeSpan _ttl = TimeSpan.FromHours(2);

    public RunStatusCache(IConnectionMultiplexer redis, ILogger<RunStatusCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SetAsync(string runId, RunStatusCacheEntry status, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"run:{runId}:status";
            var json = JsonSerializer.Serialize(status);
            await db.StringSetAsync(key, json, _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache run status for RunId={RunId}", runId);
        }
    }

    public async Task<RunStatusCacheEntry?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"run:{runId}:status";
            var json = await db.StringGetAsync(key);
            
            if (json.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<RunStatusCacheEntry>(json!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached run status for RunId={RunId}", runId);
            return null;
        }
    }

    public async Task DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"run:{runId}:status";
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cached run status for RunId={RunId}", runId);
        }
    }
}

/// <summary>
/// Cached run status entry
/// </summary>
public class RunStatusCacheEntry
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PreviewUrl { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string? PrimaryService { get; set; }
}
