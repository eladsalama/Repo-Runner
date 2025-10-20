using Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Add gRPC services
builder.Services.AddGrpc();

// Add CORS for gRPC-Web
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

app.MapGet("/", () => "RepoRunner Gateway - gRPC services available");

app.Run();
