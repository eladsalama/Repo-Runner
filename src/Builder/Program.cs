using Builder.Services;
using Shared.Streams;
using Shared.Health;
using Shared.Repositories;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

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

// Add stream consumer for RunRequested events
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<RunRequested>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Builder,
    $"builder-{hostname}");

// Add stream producer for BuildSucceeded/Failed/Progress events
builder.Services.AddStreamProducer<BuildSucceeded>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<BuildFailed>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<BuildProgress>(StreamConfig.Streams.RepoRuns);

// Add Builder services
builder.Services.AddSingleton<IGitCloner, GitCloner>();
builder.Services.AddSingleton<IDockerBuilder, DockerBuilder>();
builder.Services.AddSingleton<IDockerComposeParser, DockerComposeParser>();
builder.Services.AddSingleton<IBuildLogsRepository, BuildLogsRepository>();
builder.Services.AddSingleton<ILogRepository, LogRepository>();

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
