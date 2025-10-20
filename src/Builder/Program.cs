using Builder.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<BuilderWorker>();

var host = builder.Build();
host.Run();
