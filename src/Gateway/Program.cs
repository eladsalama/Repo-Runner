using Gateway.Services;
using Shared.Streams;
using Shared.Repositories;
using Shared.Cache;
using RepoRunner.Contracts.Events;

var builder = WebApplication.CreateBuilder(args);

// Add Redis Streams with AUTO-FLUSH on startup (Gateway is first service to start)
var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString") 
    ?? "localhost:6379";
Console.WriteLine($"[Gateway] Registering Redis with connection: {redisConnection}");
builder.Services.AddRedisStreams(redisConnection, flushOnStartup: true);
Console.WriteLine("[Gateway] Redis Streams registered");

// Add Run Status Cache
Console.WriteLine("[Gateway] Registering IRunStatusCache");
builder.Services.AddSingleton<IRunStatusCache, RunStatusCache>();
Console.WriteLine("[Gateway] IRunStatusCache registered");

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

// Add LogRepository
builder.Services.AddSingleton<ILogRepository, LogRepository>();

// Add stream producers for events
builder.Services.AddStreamProducer<RunRequested>(StreamConfig.Streams.RepoRuns);
builder.Services.AddStreamProducer<RunStopRequested>(StreamConfig.Streams.RepoRuns);

// Add gRPC services
builder.Services.AddGrpc();

// Register RunServiceImpl as singleton so it can be injected into controllers
builder.Services.AddSingleton<RunServiceImpl>();

// Add controllers for REST endpoints (MVP skeleton)
builder.Services.AddControllers();

// Add CORS for gRPC-Web and REST
builder.Services.AddCors(o => o.AddPolicy("AllowAll", builder =>
{
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader()
           .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
}));

var app = builder.Build();

// Configure gRPC-Web
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.UseCors("AllowAll");

// Map gRPC services with gRPC-Web enabled
app.MapGrpcService<RunServiceImpl>().EnableGrpcWeb();
app.MapGrpcService<InsightsServiceImpl>().EnableGrpcWeb();

// Map REST controllers (MVP skeleton)
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => new { 
    status = "healthy", 
    service = "gateway",
    timestamp = DateTime.UtcNow 
});

// Readiness check (verifies Redis and MongoDB connections)
app.MapGet("/ready", async (IRunStatusCache cache, MongoDB.Driver.IMongoDatabase db) => {
    try {
        // Quick Redis check - attempt to get a non-existent key
        _ = await cache.GetAsync("health-check");
        
        // Quick MongoDB check - ping the database
        await db.Client.GetDatabase("admin").RunCommandAsync<MongoDB.Bson.BsonDocument>(
            new MongoDB.Bson.BsonDocument("ping", 1));
        
        return Results.Ok(new { 
            status = "ready", 
            service = "gateway",
            redis = "connected",
            mongodb = "connected",
            timestamp = DateTime.UtcNow 
        });
    } catch (Exception ex) {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Service not ready");
    }
});

app.MapGet("/", () => "RepoRunner Gateway - gRPC and REST endpoints available");

app.Run();
