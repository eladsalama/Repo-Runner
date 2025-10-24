using Grpc.Core;
using RepoRunner.Contracts;
using RepoRunner.Contracts.Events;
using Shared.Streams;
using Shared.Repositories;
using Shared.Cache;
using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;

namespace Gateway.Services;

/// <summary>
/// MVP Skeleton implementation - simulates run lifecycle for testing
/// In production, this will forward to Orchestrator
/// </summary>
public class RunServiceImpl : RunService.RunServiceBase
{
    private readonly ILogger<RunServiceImpl> _logger;
    private readonly IStreamProducer<RunStopRequested> _stopProducer;
    private readonly ILogRepository _logRepository;
    private readonly IRunStatusCache _statusCache;
    private static readonly ConcurrentDictionary<string, RunState> _runs = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runCancellations = new();

    public RunServiceImpl(
        ILogger<RunServiceImpl> logger,
        IStreamProducer<RunStopRequested> stopProducer,
        ILogRepository logRepository,
        IRunStatusCache statusCache)
    {
        _logger = logger;
        _stopProducer = stopProducer;
        _logRepository = logRepository;
        _statusCache = statusCache;
    }

    public override Task<StartRunResponse> StartRun(StartRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StartRun called for repo: {RepoUrl}, mode: {Mode}", request.RepoUrl, request.Mode);
        
        var runId = Guid.NewGuid().ToString();
        var state = new RunState
        {
            RunId = runId,
            RepoUrl = request.RepoUrl,
            Branch = request.Branch,
            Mode = request.Mode,
            ComposePath = request.ComposePath,
            PrimaryService = request.PrimaryService,
            Status = RunStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        _runs[runId] = state;

        // Start simulated status transitions
        var cts = new CancellationTokenSource();
        _runCancellations[runId] = cts;
        _ = SimulateRunLifecycle(runId, cts.Token);

        return Task.FromResult(new StartRunResponse
        {
            RunId = runId,
            Status = RunStatus.Queued,
            Mode = request.Mode,
            PrimaryService = request.PrimaryService
        });
    }

    public override async Task<Empty> StopRun(StopRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StopRun called for runId: {RunId}", request.RunId);
        
        if (_runs.TryGetValue(request.RunId, out var state))
        {
            state.Status = RunStatus.Stopped;
            state.EndedAt = DateTime.UtcNow;

            if (_runCancellations.TryRemove(request.RunId, out var cts))
            {
                cts.Cancel();
            }

            // Produce RunStopRequested event for Runner to consume
            try
            {
                var stopEvent = new RunStopRequested
                {
                    RunId = request.RunId,
                    Namespace = $"run-{request.RunId}",
                    RequestedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _stopProducer.PublishAsync(stopEvent, context.CancellationToken);
                _logger.LogInformation("Produced RunStopRequested event for runId: {RunId}", request.RunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to produce RunStopRequested event for runId: {RunId}", request.RunId);
            }
        }

        return new Empty();
    }

    public override async Task<RunStatusResponse> GetRunStatus(GetRunStatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetRunStatus called for runId: {RunId}", request.RunId);
        
        // Try to get status from Redis cache first
        try
        {
            var cachedStatus = await _statusCache.GetAsync(request.RunId, context.CancellationToken);
            if (cachedStatus != null)
            {
                _logger.LogDebug("Cache hit for runId: {RunId}", request.RunId);
                return new RunStatusResponse
                {
                    RunId = cachedStatus.RunId,
                    Status = System.Enum.Parse<RepoRunner.Contracts.RunStatus>(cachedStatus.Status, ignoreCase: true),
                    PreviewUrl = cachedStatus.PreviewUrl ?? "",
                    StartedAt = cachedStatus.StartedAt.HasValue ? Timestamp.FromDateTime(cachedStatus.StartedAt.Value) : null,
                    EndedAt = cachedStatus.EndedAt.HasValue ? Timestamp.FromDateTime(cachedStatus.EndedAt.Value) : null,
                    ErrorMessage = cachedStatus.ErrorMessage ?? "",
                    Mode = System.Enum.Parse<RepoRunner.Contracts.RunMode>(cachedStatus.Mode, ignoreCase: true),
                    PrimaryService = cachedStatus.PrimaryService ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get status from cache for runId: {RunId}, falling back to in-memory", request.RunId);
        }

        // Fallback to in-memory store (for MVP/testing)
        if (_runs.TryGetValue(request.RunId, out var state))
        {
            return new RunStatusResponse
            {
                RunId = state.RunId,
                Status = state.Status,
                PreviewUrl = state.PreviewUrl ?? "",
                StartedAt = state.StartedAt.HasValue ? Timestamp.FromDateTime(state.StartedAt.Value) : null,
                EndedAt = state.EndedAt.HasValue ? Timestamp.FromDateTime(state.EndedAt.Value) : null,
                ErrorMessage = state.ErrorMessage ?? "",
                Mode = state.Mode,
                PrimaryService = state.PrimaryService ?? ""
            };
        }

        throw new RpcException(new Status(StatusCode.NotFound, $"Run {request.RunId} not found"));
    }

    public override async Task StreamLogs(StreamLogsRequest request, IServerStreamWriter<LogEntry> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("StreamLogs called for runId: {RunId}, source: {Source}, service: {ServiceName}",
            request.RunId, request.Source, request.ServiceName);

        try
        {
            // Map LogSource enum to string for repository query
            string? sourceFilter = request.Source switch
            {
                LogSource.Build => "build",
                LogSource.Run => "run",
                _ => null
            };

            // Stream logs from MongoDB
            await foreach (var log in _logRepository.GetLogsAsync(
                request.RunId,
                sourceFilter,
                string.IsNullOrEmpty(request.ServiceName) ? null : request.ServiceName,
                context.CancellationToken))
            {
                var logEntry = new LogEntry
                {
                    Timestamp = Timestamp.FromDateTime(log.Timestamp),
                    Source = log.Source == "build" ? LogSource.Build : LogSource.Run,
                    Line = log.Line
                };

                await responseStream.WriteAsync(logEntry, context.CancellationToken);
            }

            _logger.LogInformation("Completed streaming logs for runId: {RunId}", request.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming logs for runId: {RunId}", request.RunId);
            throw new RpcException(new Status(StatusCode.Internal, $"Error streaming logs: {ex.Message}"));
        }
    }

    /// <summary>
    /// Simulates run lifecycle: Queued → Building (2s) → Running (3s) → Succeeded
    /// </summary>
    private async Task SimulateRunLifecycle(string runId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_runs.TryGetValue(runId, out var state)) return;

            // Queued → Building
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            
            state.Status = RunStatus.Building;
            _logger.LogInformation("Run {RunId} transitioned to Building", runId);

            // Building → Running
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            
            state.Status = RunStatus.Running;
            state.StartedAt = DateTime.UtcNow;
            state.PreviewUrl = $"http://localhost:8080/run/{runId}";
            _logger.LogInformation("Run {RunId} transitioned to Running", runId);

            // Running → Succeeded
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            
            state.Status = RunStatus.Succeeded;
            state.EndedAt = DateTime.UtcNow;
            _logger.LogInformation("Run {RunId} transitioned to Succeeded", runId);

            _runCancellations.TryRemove(runId, out _);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Run {RunId} simulation cancelled", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating run {RunId}", runId);
            if (_runs.TryGetValue(runId, out var state))
            {
                state.Status = RunStatus.Failed;
                state.ErrorMessage = ex.Message;
                state.EndedAt = DateTime.UtcNow;
            }
        }
    }
}

/// <summary>
/// Holds state for a run (MVP skeleton - in production this will be in Mongo)
/// </summary>
internal class RunState
{
    public string RunId { get; set; } = "";
    public string RepoUrl { get; set; } = "";
    public string Branch { get; set; } = "";
    public RepoRunner.Contracts.RunMode Mode { get; set; }
    public string ComposePath { get; set; } = "";
    public string? PrimaryService { get; set; }
    public RunStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? PreviewUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
