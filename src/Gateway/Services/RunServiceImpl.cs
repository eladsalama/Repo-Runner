using Grpc.Core;
using RepoRunner.Contracts;
using RepoRunner.Contracts.Events;
using Shared.Streams;
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
    private static readonly ConcurrentDictionary<string, RunState> _runs = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runCancellations = new();

    public RunServiceImpl(
        ILogger<RunServiceImpl> logger,
        IStreamProducer<RunStopRequested> stopProducer)
    {
        _logger = logger;
        _stopProducer = stopProducer;
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

    public override Task<RunStatusResponse> GetRunStatus(GetRunStatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetRunStatus called for runId: {RunId}", request.RunId);
        
        if (_runs.TryGetValue(request.RunId, out var state))
        {
            return Task.FromResult(new RunStatusResponse
            {
                RunId = state.RunId,
                Status = state.Status,
                PreviewUrl = state.PreviewUrl ?? "",
                StartedAt = state.StartedAt.HasValue ? Timestamp.FromDateTime(state.StartedAt.Value) : null,
                EndedAt = state.EndedAt.HasValue ? Timestamp.FromDateTime(state.EndedAt.Value) : null,
                ErrorMessage = state.ErrorMessage ?? "",
                Mode = state.Mode,
                PrimaryService = state.PrimaryService ?? ""
            });
        }

        throw new RpcException(new Status(StatusCode.NotFound, $"Run {request.RunId} not found"));
    }

    public override Task StreamLogs(StreamLogsRequest request, IServerStreamWriter<LogEntry> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("StreamLogs called for runId: {RunId}", request.RunId);
        
        // MVP skeleton: no actual logs yet
        throw new RpcException(new Status(StatusCode.Unimplemented, "StreamLogs not yet implemented in MVP skeleton"));
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
