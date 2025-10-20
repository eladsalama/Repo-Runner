using Orchestrator.Services;
using Shared.Streams;
using RepoRunner.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

// Add Redis Streams
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
builder.Services.AddRedisStreams(redisConnection);

// Add stream producer for RunRequested events
builder.Services.AddStreamProducer<RunRequested>(StreamConfig.Streams.RepoRuns);

// Add MongoDB
var mongoConnection = builder.Configuration.GetValue<string>("MongoDB:ConnectionString")
    ?? "mongodb://localhost:27017";
var mongoDatabase = builder.Configuration.GetValue<string>("MongoDB:Database")
    ?? "reporunner";
builder.Services.AddSingleton<MongoDB.Driver.IMongoClient>(sp =>
    new MongoDB.Driver.MongoClient(mongoConnection));
builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<MongoDB.Driver.IMongoClient>();
    return client.GetDatabase(mongoDatabase);
});

builder.Services.AddHostedService<OrchestratorWorker>();

var host = builder.Build();
host.Run();
