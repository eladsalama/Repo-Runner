# Shared Library

Common abstractions and utilities for RepoRunner services.

## Streams (`Shared.Streams`)

Redis Streams-based event pipeline for coordinating between microservices.

### Interfaces

- **`IStreamProducer<T>`**: Publish events to Redis Streams
- **`IStreamConsumer<T>`**: Consume events with consumer groups, automatic retries, and DLQ

### Configuration

**Stream Names:**
- `stream:repo-runs` - Main run lifecycle events (RunRequested, BuildSucceeded, RunSucceeded, etc.)
- `stream:indexing` - Indexing-related events

**Consumer Groups:**
- `group:builder` - Builder service consumes RunRequested
- `group:runner` - Runner service consumes BuildSucceeded
- `group:indexer` - Indexer service consumes RunRequested (for indexing)

**Dead Letter Queue:**
- `list:dlq` - Failed messages after max retries (default: 3 attempts)

### Features

- **Automatic Retries**: Messages that fail processing are automatically retried
- **Dead Letter Queue**: After 3 failed attempts, messages are moved to DLQ for manual inspection
- **Consumer Groups**: Multiple instances of the same service share the load
- **Idle Message Claiming**: Stale messages (idle > 60s) are automatically claimed by healthy consumers
- **Health Checks**: Track consumer lag and stream health

### Usage

#### Producer (Orchestrator)

```csharp
using Shared.Streams;
using RepoRunner.Contracts.Events;

// In Program.cs
builder.Services.AddRedisStreams("localhost:6379");
builder.Services.AddStreamProducer<RunRequested>(StreamConfig.Streams.RepoRuns);

// In service
public class OrchestratorService
{
    private readonly IStreamProducer<RunRequested> _producer;
    
    public async Task StartRunAsync(string runId, string repoUrl)
    {
        var event = new RunRequested
        {
            RunId = runId,
            RepoUrl = repoUrl,
            Branch = "main",
            RequestedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await _producer.PublishAsync(event);
    }
}
```

#### Consumer (Builder)

```csharp
using Shared.Streams;
using RepoRunner.Contracts.Events;

// In Program.cs
builder.Services.AddRedisStreams("localhost:6379");
builder.Services.AddStreamConsumer<RunRequested>(
    StreamConfig.Streams.RepoRuns,
    StreamConfig.Groups.Builder,
    "builder-instance-1");

// In BackgroundService
public class BuilderWorker : BackgroundService
{
    private readonly IStreamConsumer<RunRequested> _consumer;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _consumer.ConsumeAsync(async (runRequested) =>
        {
            // Process the event
            await BuildRepo(runRequested.RunId, runRequested.RepoUrl);
            
            // Return true to acknowledge, false to retry
            return true;
        }, stoppingToken);
    }
}
```

#### Health Checks

```csharp
using Shared.Health;

builder.Services.AddHealthChecks()
    .AddStreamLagCheck<RunRequested>(
        "builder-stream-lag",
        StreamConfig.Streams.RepoRuns,
        warningThreshold: 50,
        unhealthyThreshold: 200);
```

### Monitoring

Check consumer lag:
```csharp
var lag = await consumer.GetLagAsync();
// lag = number of pending messages in consumer group
```

View DLQ messages (Redis CLI):
```bash
redis-cli LRANGE list:dlq 0 -1
```

## Health (`Shared.Health`)

Health check utilities for monitoring service health.

### Stream Lag Health Check

Monitors consumer lag and reports:
- **Healthy**: Lag < warning threshold
- **Degraded**: Lag >= warning threshold but < unhealthy threshold
- **Unhealthy**: Lag >= unhealthy threshold

## Dependencies

- **StackExchange.Redis** 2.9.32 - Redis client
- **Google.Protobuf** 3.33.0 - Protobuf serialization
- **Microsoft.Extensions.Diagnostics.HealthChecks** 9.0.10 - Health checks
