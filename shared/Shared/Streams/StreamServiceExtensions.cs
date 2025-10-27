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
    /// Add Redis connection multiplexer and cleanup service
    /// </summary>
    /// <param name="flushOnStartup">If true, flushes all streams on startup to ensure clean state. Use only in ONE service (e.g., Gateway) to avoid race conditions.</param>
    public static IServiceCollection AddRedisStreams(
        this IServiceCollection services,
        string connectionString,
        bool flushOnStartup = false)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
            try
            {
                var config = ConfigurationOptions.Parse(connectionString);
                config.AbortOnConnectFail = false; // Don't throw on startup, retry in background
                var connection = ConnectionMultiplexer.Connect(config);
                logger.LogInformation("Redis connected to {ConnectionString}", connectionString);
                return connection;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to Redis at {ConnectionString}. Services may be degraded.", connectionString);
                throw; // Rethrow to fail DI and make debugging obvious
            }
        });

        // Add automatic cleanup service
        services.AddSingleton<StreamCleanupService>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<StreamCleanupService>>();
            var lifetime = sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            return new StreamCleanupService(redis, logger, lifetime, flushOnStartup);
        });
        services.AddHostedService(sp => sp.GetRequiredService<StreamCleanupService>());

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
