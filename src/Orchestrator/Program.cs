using Orchestrator.Services;
using Shared.Streams;
using Shared.Health;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add stream producer for RunRequested events
builder.Services.AddStreamProducer<RunRequested>(StreamConfig.Streams.RepoRuns);

// Add stream producer for RunStopRequested events
builder.Services.AddStreamProducer<RunStopRequested>(StreamConfig.Streams.RepoRuns);

// Add stream consumer for BuildSucceeded/BuildFailed events
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<BuildSucceeded>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Orchestrator,
    $"orchestrator-{hostname}");
builder.Services.AddStreamConsumer<BuildFailed>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Orchestrator,
    $"orchestrator-{hostname}");

// Add MongoDB
var mongoConnection = builder.Configuration.GetValue<string>("MongoDB:ConnectionString")
    ?? "mongodb://localhost:27017";
var mongoDatabase = builder.Configuration.GetValue<string>("MongoDB:Database")
    ?? "reporunner";
builder.Services.AddSingleton<MongoDB.Driver.IMongoClient>(sp =>
    new MongoDB.Driver.MongoClient(mongoConnection));
builder.Services.AddSingleton<MongoDB.Driver.IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<MongoDB.Driver.IMongoClient>();
    return client.GetDatabase(mongoDatabase);
});

// Add Orchestrator services
builder.Services.AddSingleton<IRunRepository, RunRepository>();
builder.Services.AddSingleton<IStopRunService, StopRunService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddStreamLagCheck<BuildSucceeded>(
        "orchestrator-buildsucceeded-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200)
    .AddStreamLagCheck<BuildFailed>(
        "orchestrator-buildfailed-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200);

builder.Services.AddHostedService<OrchestratorWorker>();

var host = builder.Build();
host.Run();
