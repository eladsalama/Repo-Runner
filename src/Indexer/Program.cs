using Indexer.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<IndexerWorker>();

var host = builder.Build();
host.Run();
