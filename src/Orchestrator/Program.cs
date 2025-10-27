using Orchestrator.Services;
using Shared.Streams;
using Shared.Health;
using Shared.Cache;
using Shared.Repositories;
using RepoRunner.Contracts.Events;
using MongoDB.Driver;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;

// Configure MongoDB to serialize enums as strings globally
var pack = new ConventionPack
{
    new EnumRepresentationConvention(MongoDB.Bson.BsonType.String)
};
ConventionRegistry.Register("EnumStringConvention", pack, t => true);

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add Run Status Cache
builder.Services.AddSingleton<IRunStatusCache, RunStatusCache>();

var hostname = Environment.MachineName;

// Add stream consumer for RunRequested events (to create run records)
builder.Services.AddStreamConsumer<RunRequested>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:runrequested",
    $"orchestrator-runrequested-{hostname}");

// Add stream consumer for BuildSucceeded/BuildFailed/BuildProgress events
builder.Services.AddStreamConsumer<BuildSucceeded>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:buildsucceeded",
    $"orchestrator-buildsucceeded-{hostname}");
builder.Services.AddStreamConsumer<BuildFailed>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:buildfailed",
    $"orchestrator-buildfailed-{hostname}");
builder.Services.AddStreamConsumer<BuildProgress>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:buildprogress",
    $"orchestrator-buildprogress-{hostname}");

// Add stream consumer for RunSucceeded/RunFailed events
builder.Services.AddStreamConsumer<RunSucceeded>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:runsucceeded",
    $"orchestrator-runsucceeded-{hostname}");
builder.Services.AddStreamConsumer<RunFailed>(
    StreamConfig.Streams.RepoRuns,
    $"{StreamConfig.Groups.Orchestrator}:runfailed",
    $"orchestrator-runfailed-{hostname}");

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
