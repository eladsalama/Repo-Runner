using Google.Protobuf;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Streams;

/// <summary>
/// Redis Streams implementation of IStreamConsumer with retry and DLQ support
/// </summary>
public class RedisStreamConsumer<T> : IStreamConsumer<T> where T : class, IMessage, new()
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _streamName;
    private readonly string _groupName;
    private readonly string _consumerName;
    private readonly ILogger<RedisStreamConsumer<T>> _logger;

    public RedisStreamConsumer(
        IConnectionMultiplexer redis,
        string streamName,
        string groupName,
        string consumerName,
        ILogger<RedisStreamConsumer<T>> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        _groupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
        _consumerName = consumerName ?? throw new ArgumentNullException(nameof(consumerName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConsumeAsync(Func<T, Task<bool>> handler, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // Ensure consumer group exists
        await EnsureConsumerGroupAsync(db);

        _logger.LogInformation(
            "Starting consumer {ConsumerName} for group {GroupName} on stream {StreamName}",
            _consumerName, _groupName, _streamName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // First, claim and process any pending messages that have been idle
                await ClaimAndProcessPendingAsync(db, handler, cancellationToken);

                // Then read new messages
                var entries = await db.StreamReadGroupAsync(
                    _streamName,
                    _groupName,
                    _consumerName,
                    ">",
                    count: 10);

                foreach (var entry in entries)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    await ProcessMessageAsync(db, entry, handler, cancellationToken);
                }

                // If no messages, wait a bit before polling again
                if (entries.Length == 0)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Consumer {ConsumerName} stopped", _consumerName);
                break;
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
            {
                _logger.LogWarning(ex, "Consumer group {GroupName} was deleted, recreating...", _groupName);
                await EnsureConsumerGroupAsync(db);
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in consumer loop for {ConsumerName}", _consumerName);
                await Task.Delay(5000, cancellationToken); // Back off on error
            }
        }
    }

    public async Task<long> GetLagAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pendingInfo = await db.StreamPendingAsync(_streamName, _groupName);
            return pendingInfo.PendingMessageCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lag for group {GroupName}", _groupName);
            return -1;
        }
    }

    private async Task EnsureConsumerGroupAsync(IDatabase db)
    {
        try
        {
            // Try to create the group; if it exists, this will throw
            await db.StreamCreateConsumerGroupAsync(_streamName, _groupName, StreamPosition.NewMessages);
            _logger.LogInformation("Created consumer group {GroupName} for stream {StreamName}", _groupName, _streamName);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Group already exists, which is fine
            _logger.LogDebug("Consumer group {GroupName} already exists", _groupName);
        }
    }

    private async Task ClaimAndProcessPendingAsync(IDatabase db, Func<T, Task<bool>> handler, CancellationToken cancellationToken)
    {
        try
        {
            // Get pending messages that have been idle for too long
            var pendingMessages = await db.StreamPendingMessagesAsync(
                _streamName,
                _groupName,
                count: 10,
                consumerName: RedisValue.Null);

            foreach (var pending in pendingMessages)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // If message has been idle longer than threshold, claim it
                if (pending.IdleTimeInMilliseconds > StreamConfig.IdleTimeoutMs)
                {
                    _logger.LogWarning(
                        "Claiming idle message {MessageId} (idle: {IdleTime}ms, delivery count: {DeliveryCount})",
                        pending.MessageId, pending.IdleTimeInMilliseconds, pending.DeliveryCount);

                    // Check if we should send to DLQ
                    if (pending.DeliveryCount >= StreamConfig.MaxRetryAttempts)
                    {
                        await SendToDeadLetterQueueAsync(db, pending.MessageId);
                        continue;
                    }

                    // Claim the message
                    var claimed = await db.StreamClaimAsync(_streamName, _groupName, _consumerName, 
                        minIdleTimeInMs: 0, messageIds: new[] { pending.MessageId });

                    foreach (var entry in claimed)
                    {
                        await ProcessMessageAsync(db, entry, handler, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error claiming pending messages");
        }
    }

    private async Task ProcessMessageAsync(IDatabase db, StreamEntry entry, Func<T, Task<bool>> handler, CancellationToken cancellationToken)
    {
        try
        {
            // Check message type first - skip if not our type
            var typeValue = entry.Values.FirstOrDefault(v => v.Name == "type").Value;
            var expectedType = typeof(T).Name;
            if (!typeValue.IsNullOrEmpty && (string)typeValue! != expectedType)
            {
                // Not our message type, acknowledge and skip
                await db.StreamAcknowledgeAsync(_streamName, _groupName, entry.Id);
                _logger.LogDebug("Skipping message {MessageId} of type {ActualType} (expected {ExpectedType})", 
                    entry.Id, (string)typeValue!, expectedType);
                return;
            }
            
            // Extract payload from stream entry
            var payloadValue = entry.Values.FirstOrDefault(v => v.Name == "payload").Value;
            if (payloadValue.IsNullOrEmpty)
            {
                _logger.LogWarning("Message {MessageId} has no payload, acknowledging anyway", entry.Id);
                await db.StreamAcknowledgeAsync(_streamName, _groupName, entry.Id);
                return;
            }

            // Deserialize protobuf message from Base64-encoded payload
            var base64Payload = (string)payloadValue!;
            var payloadBytes = Convert.FromBase64String(base64Payload);
            var message = new T();
            message.MergeFrom(payloadBytes);

            _logger.LogDebug("Processing message {MessageId} of type {Type}", entry.Id, typeof(T).Name);

            // Call handler
            bool success = await handler(message);

            if (success)
            {
                // Acknowledge the message
                await db.StreamAcknowledgeAsync(_streamName, _groupName, entry.Id);
                _logger.LogDebug("Acknowledged message {MessageId}", entry.Id);
            }
            else
            {
                _logger.LogWarning("Handler returned false for message {MessageId}, will retry", entry.Id);
                // Don't acknowledge - let it be claimed later
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", entry.Id);
            // Don't acknowledge - let it be retried
        }
    }

    private async Task SendToDeadLetterQueueAsync(IDatabase db, RedisValue messageId)
    {
        try
        {
            // Read the message one last time
            var messages = await db.StreamReadAsync(_streamName, messageId, count: 1);
            if (messages.Length > 0)
            {
                var entry = messages[0];
                var dlqEntry = $"{_streamName}:{messageId}:" + string.Join(",", 
                    entry.Values.Select(v => $"{v.Name}={v.Value}"));

                await db.ListLeftPushAsync(StreamConfig.DeadLetterQueue, dlqEntry);
                
                _logger.LogWarning(
                    "Sent message {MessageId} to DLQ after {MaxAttempts} attempts",
                    messageId, StreamConfig.MaxRetryAttempts);
            }

            // Acknowledge to remove from pending
            await db.StreamAcknowledgeAsync(_streamName, _groupName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId} to DLQ", messageId);
        }
    }
}
