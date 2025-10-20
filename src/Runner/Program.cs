using Runner.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<RunnerWorker>();

var host = builder.Build();
host.Run();
