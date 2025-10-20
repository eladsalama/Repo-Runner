namespace Shared.Streams;

/// <summary>
/// Abstraction for producing events to Redis Streams
/// </summary>
public interface IStreamProducer<T> where T : class
{
    /// <summary>
    /// Publish an event to the stream
    /// </summary>
    /// <param name="event">Event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message ID in the stream</returns>
    Task<string> PublishAsync(T @event, CancellationToken cancellationToken = default);
}
