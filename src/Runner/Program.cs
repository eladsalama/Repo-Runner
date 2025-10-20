using Runner.Services;
using Shared.Streams;
using Shared.Health;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add stream consumer for BuildSucceeded events
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<BuildSucceeded>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Runner,
    $"runner-{hostname}");

// Add stream producer for RunSucceeded/Failed events
builder.Services.AddStreamProducer<RunSucceeded>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<RunFailed>(StreamConfig.Streams.RepoRuns);

// Add health checks
builder.Services.AddHealthChecks()
    .AddStreamLagCheck<BuildSucceeded>(
        "runner-stream-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200);

builder.Services.AddHostedService<RunnerWorker>();

var host = builder.Build();
host.Run();
