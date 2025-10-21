using Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => "RepoRunner Gateway - gRPC and REST endpoints available");

app.Run();
