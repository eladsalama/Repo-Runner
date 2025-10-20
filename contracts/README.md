# RepoRunner Protobuf Contracts

Shared `.proto` definitions for service-to-service communication (gRPC) and extension-to-gateway (gRPC-Web).

## Files

- `run.proto` - RunService: StartRun, StopRun, GetRunStatus, StreamLogs
- `insights.proto` - InsightsService: AskRepo with RAG citations
- `events.proto` - Event messages for Redis Streams (RunRequested, BuildSucceeded, etc.)

## Code Generation

### .NET Services

```bash
# Add to each service project:
dotnet add package Grpc.AspNetCore
dotnet add package Grpc.Tools
dotnet add package Google.Protobuf

# Reference protos in .csproj:
<ItemGroup>
  <Protobuf Include="..\..\contracts\*.proto" GrpcServices="Server" />
</ItemGroup>
```

### TypeScript Extension

```bash
# Install tools:
npm install -g grpc-tools grpc_tools_node_protoc_ts
npm install google-protobuf @improbable-eng/grpc-web

# Generate:
protoc --js_out=import_style=commonjs,binary:./extension/generated \
       --grpc-web_out=import_style=typescript,mode=grpcwebtext:./extension/generated \
       --proto_path=./contracts \
       ./contracts/*.proto
```

## gRPC-Web Gateway

The Gateway service terminates gRPC-Web connections from the browser extension and proxies to internal gRPC services.

```csharp
// In Gateway/Program.cs:
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<RunServiceImpl>().EnableGrpcWeb();
app.MapGrpcService<InsightsServiceImpl>().EnableGrpcWeb();
```

## Notes

- Event messages (events.proto) are serialized to JSON for Redis Streams, not used directly as gRPC services.
- All timestamps use `google.protobuf.Timestamp` for timezone-safe serialization.
- gRPC-Web annotations (`google/api/annotations.proto`) allow HTTP/1.1 access from browsers.
