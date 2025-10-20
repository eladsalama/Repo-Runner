namespace Shared.Streams;

/// <summary>
/// Abstraction for consuming events from Redis Streams with consumer groups
/// </summary>
public interface IStreamConsumer<T> where T : class
{
    /// <summary>
    /// Start consuming messages from the stream
    /// </summary>
    /// <param name="handler">Handler for processing messages</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConsumeAsync(Func<T, Task<bool>> handler, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get consumer group lag (number of pending messages)
    /// </summary>
    Task<long> GetLagAsync(CancellationToken cancellationToken = default);
}
