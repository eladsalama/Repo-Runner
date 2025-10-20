using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Google.Protobuf;

namespace Shared.Streams;

/// <summary>
/// Extension methods for registering Redis Streams services
/// </summary>
public static class StreamServiceExtensions
{
    /// <summary>
    /// Add Redis connection multiplexer
    /// </summary>
    public static IServiceCollection AddRedisStreams(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(connectionString);
            return ConnectionMultiplexer.Connect(config);
        });

        return services;
    }

    /// <summary>
    /// Add a stream producer for a specific event type
    /// </summary>
    public static IServiceCollection AddStreamProducer<T>(
        this IServiceCollection services,
        string streamName) where T : class, IMessage
    {
        services.AddSingleton<IStreamProducer<T>>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisStreamProducer<T>>>();
            return new RedisStreamProducer<T>(redis, streamName, logger);
        });

        return services;
    }

    /// <summary>
    /// Add a stream consumer for a specific event type
    /// </summary>
    public static IServiceCollection AddStreamConsumer<T>(
        this IServiceCollection services,
        string streamName,
        string groupName,
        string consumerName) where T : class, IMessage, new()
    {
        services.AddSingleton<IStreamConsumer<T>>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisStreamConsumer<T>>>();
            return new RedisStreamConsumer<T>(redis, streamName, groupName, consumerName, logger);
        });

        return services;
    }
}
