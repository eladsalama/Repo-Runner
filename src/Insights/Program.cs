using Insights.Services;

var builder = WebApplication.CreateBuilder(args);

// Add gRPC services
builder.Services.AddGrpc();

var app = builder.Build();

// Map gRPC service
app.MapGrpcService<InsightsServiceImpl>();

app.Run();
