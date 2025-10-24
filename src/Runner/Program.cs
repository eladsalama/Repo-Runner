using Runner.Services;
using Shared.Streams;
using Shared.Health;
using Shared.Repositories;
using RepoRunner.Contracts.Events;
using k8s;

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

// Add Kubernetes client
builder.Services.AddSingleton<IKubernetes>(sp =>
{
    var config = KubernetesClientConfiguration.BuildDefaultConfig();
    return new Kubernetes(config);
});

// Add stream consumer for BuildSucceeded events
var hostname = Environment.MachineName;
builder.Services.AddStreamConsumer<BuildSucceeded>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Runner,
    $"runner-{hostname}");

// Add stream consumer for RunStopRequested events
builder.Services.AddStreamConsumer<RunStopRequested>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Runner,
    $"runner-{hostname}");

// Add stream producers for RunSucceeded/RunFailed events
builder.Services.AddStreamProducer<RunSucceeded>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<RunFailed>(StreamConfig.Streams.RepoRuns);

// Add Runner services
builder.Services.AddSingleton<IKubernetesResourceGenerator, KubernetesResourceGenerator>();
builder.Services.AddSingleton<IKubernetesDeployer, KubernetesDeployer>();
builder.Services.AddSingleton<IRunRepository, RunRepository>();
builder.Services.AddSingleton<ILogRepository, LogRepository>();
builder.Services.AddSingleton<IPodLogTailer, PodLogTailer>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddStreamLagCheck<BuildSucceeded>(
        "runner-buildsucceeded-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200)
    .AddStreamLagCheck<RunStopRequested>(
        "runner-stoproequested-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200);

builder.Services.AddHostedService<RunnerWorker>();
builder.Services.AddHostedService<StopRunnerWorker>();
builder.Services.AddHostedService<NamespaceCleanupService>();

var host = builder.Build();
host.Run();
