namespace Shared.Streams;

/// <summary>
/// Interface for cleaning up Redis streams and consumer groups
/// </summary>
public interface IStreamCleanup
{
    /// <summary>
    /// Acknowledges all pending messages in a consumer group to unblock the queue
    /// </summary>
    Task AcknowledgeAllPendingAsync(string streamName, string groupName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Trims old messages from a stream, keeping only recent messages
    /// </summary>
    Task TrimStreamAsync(string streamName, int maxLength = 1000, CancellationToken cancellationToken = default);
}
