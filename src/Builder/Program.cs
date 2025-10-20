using Builder.Services;
using Shared.Streams;
using Shared.Health;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add stream consumer for RunRequested events
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<RunRequested>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Builder,
    $"builder-{hostname}");

// Add stream producer for BuildSucceeded/Failed events
builder.Services.AddStreamProducer<BuildSucceeded>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<BuildFailed>(StreamConfig.Streams.RepoRuns);

// Add health checks
builder.Services.AddHealthChecks()
    .AddStreamLagCheck<RunRequested>(
        "builder-stream-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200);

builder.Services.AddHostedService<BuilderWorker>();

var host = builder.Build();
host.Run();
