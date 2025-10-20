using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Streams;
using Google.Protobuf;

namespace Shared.Health;

/// <summary>
/// Health check for Redis Stream consumer lag
/// </summary>
public class StreamLagHealthCheck<T> : IHealthCheck where T : class, IMessage, new()
{
    private readonly IStreamConsumer<T> _consumer;
    private readonly string _streamName;
    private readonly long _warningThreshold;
    private readonly long _unhealthyThreshold;

    public StreamLagHealthCheck(
        IStreamConsumer<T> consumer,
        string streamName,
        long warningThreshold = 100,
        long unhealthyThreshold = 1000)
    {
        _consumer = consumer;
        _streamName = streamName;
        _warningThreshold = warningThreshold;
        _unhealthyThreshold = unhealthyThreshold;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lag = await _consumer.GetLagAsync(cancellationToken);

            if (lag < 0)
            {
                return HealthCheckResult.Degraded(
                    $"Unable to get lag for stream {_streamName}");
            }

            if (lag >= _unhealthyThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Stream {_streamName} lag is {lag} (threshold: {_unhealthyThreshold})");
            }

            if (lag >= _warningThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Stream {_streamName} lag is {lag} (threshold: {_warningThreshold})");
            }

            return HealthCheckResult.Healthy(
                $"Stream {_streamName} lag is {lag}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Error checking stream {_streamName} lag",
                ex);
        }
    }
}
