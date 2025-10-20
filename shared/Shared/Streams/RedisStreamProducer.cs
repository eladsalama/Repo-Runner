using Google.Protobuf;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Streams;

/// <summary>
/// Redis Streams implementation of IStreamProducer
/// </summary>
public class RedisStreamProducer<T> : IStreamProducer<T> where T : class, IMessage
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _streamName;
    private readonly ILogger<RedisStreamProducer<T>> _logger;

    public RedisStreamProducer(
        IConnectionMultiplexer redis,
        string streamName,
        ILogger<RedisStreamProducer<T>> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> PublishAsync(T @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        try
        {
            var db = _redis.GetDatabase();
            
            // Serialize protobuf message to bytes
            var payload = @event.ToByteArray();
            
            // Add to stream with payload as a single field
            var nameValueEntries = new NameValueEntry[]
            {
                new NameValueEntry("type", @event.GetType().Name),
                new NameValueEntry("payload", payload)
            };

            var messageId = await db.StreamAddAsync(_streamName, nameValueEntries);
            
            _logger.LogInformation(
                "Published {EventType} to stream {StreamName} with ID {MessageId}",
                @event.GetType().Name,
                _streamName,
                messageId);

            return messageId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to stream {StreamName}", _streamName);
            throw;
        }
    }
}
