using Indexer.Services;
using Shared.Streams;
using Shared.Health;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add stream consumer for RunRequested events (for indexing)
// MUST consume from stream:repo-runs (same stream as Builder/Orchestrator) with different group
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<RunRequested>(
    StreamConfig.Streams.RepoRuns,  // FIX: Use RepoRuns stream, not Indexing
    StreamConfig.Groups.Indexer,
    $"indexer-{hostname}");

// Add stream producer for IndexingComplete events (future use)
builder.Services.AddStreamProducer<IndexingComplete>(StreamConfig.Streams.Indexing);

// Add health checks
builder.Services.AddHealthChecks()
    .AddStreamLagCheck<RunRequested>(
        "indexer-stream-lag",
        StreamConfig.Streams.RepoRuns,  // FIX: Monitor correct stream
        warningThreshold: 50,
        unhealthyThreshold: 200);

builder.Services.AddHostedService<IndexerWorker>();

var host = builder.Build();
host.Run();
