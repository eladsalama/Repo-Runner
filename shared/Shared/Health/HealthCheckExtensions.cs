using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Google.Protobuf;

namespace Shared.Health;

/// <summary>
/// Extension methods for adding stream lag health checks
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Add a health check for stream consumer lag
    /// </summary>
    public static IHealthChecksBuilder AddStreamLagCheck<T>(
        this IHealthChecksBuilder builder,
        string name,
        string streamName,
        long warningThreshold = 100,
        long unhealthyThreshold = 1000,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
        where T : class, IMessage, new()
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var consumer = sp.GetRequiredService<Streams.IStreamConsumer<T>>();
                return new StreamLagHealthCheck<T>(
                    consumer,
                    streamName,
                    warningThreshold,
                    unhealthyThreshold);
            },
            failureStatus,
            tags));
    }
}
