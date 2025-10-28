using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Streams;

/// <summary>
/// Implementation of stream cleanup operations for Redis
/// </summary>
public class RedisStreamCleanup : IStreamCleanup
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamCleanup> _logger;

    public RedisStreamCleanup(IConnectionMultiplexer redis, ILogger<RedisStreamCleanup> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AcknowledgeAllPendingAsync(string streamName, string groupName, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Get all pending messages
            var pendingInfo = await db.StreamPendingAsync(streamName, groupName);
            
            if (pendingInfo.PendingMessageCount == 0)
            {
                _logger.LogDebug("No pending messages in group {GroupName} for stream {StreamName}", groupName, streamName);
                return;
            }

            _logger.LogInformation(
                "Found {Count} pending messages in group {GroupName} for stream {StreamName}, acknowledging all...",
                pendingInfo.PendingMessageCount, groupName, streamName);

            // Get detailed pending messages
            var pendingMessages = await db.StreamPendingMessagesAsync(
                streamName, 
                groupName, 
                (int)pendingInfo.PendingMessageCount, 
                RedisValue.Null);

            // Acknowledge all pending messages
            int acknowledgedCount = 0;
            foreach (var pending in pendingMessages)
            {
                await db.StreamAcknowledgeAsync(streamName, groupName, pending.MessageId);
                acknowledgedCount++;
            }

            _logger.LogInformation(
                "✅ Acknowledged {Count} pending messages in group {GroupName} for stream {StreamName}",
                acknowledgedCount, groupName, streamName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to acknowledge pending messages in group {GroupName} for stream {StreamName}",
                groupName, streamName);
            throw;
        }
    }

    public async Task TrimStreamAsync(string streamName, int maxLength = 1000, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var trimmed = await db.StreamTrimAsync(streamName, maxLength, useApproximateMaxLength: true);
            
            if (trimmed > 0)
            {
                _logger.LogInformation(
                    "Trimmed {Count} old messages from stream {StreamName}, keeping last {MaxLength}",
                    trimmed, streamName, maxLength);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trim stream {StreamName}", streamName);
            throw;
        }
    }
}
