using Orchestrator.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<OrchestratorWorker>();

var host = builder.Build();
host.Run();
