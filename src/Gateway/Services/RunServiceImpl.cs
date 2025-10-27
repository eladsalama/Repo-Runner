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
/// Gateway RunService implementation - forwards requests to Orchestrator via events
/// </summary>
public class RunServiceImpl : RunService.RunServiceBase
{
    private readonly ILogger<RunServiceImpl> _logger;
    private readonly IStreamProducer<RunRequested> _runProducer;
    private readonly IStreamProducer<RunStopRequested> _stopProducer;
    private readonly ILogRepository _logRepository;
    private readonly IRunStatusCache _statusCache;

    public RunServiceImpl(
        ILogger<RunServiceImpl> logger,
        IStreamProducer<RunRequested> runProducer,
        IStreamProducer<RunStopRequested> stopProducer,
        ILogRepository logRepository,
        IRunStatusCache statusCache)
    {
        _logger = logger;
        _runProducer = runProducer;
        _stopProducer = stopProducer;
        _logRepository = logRepository;
        _statusCache = statusCache;
    }

    public override async Task<StartRunResponse> StartRun(StartRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StartRun called for repo: {RepoUrl}, mode: {Mode}", request.RepoUrl, request.Mode);
        
        var runId = Guid.NewGuid().ToString();
        
        // Convert RunMode from contracts to events (they're in different namespaces)
        var eventMode = request.Mode == RepoRunner.Contracts.RunMode.Compose 
            ? RepoRunner.Contracts.Events.RunMode.Compose 
            : RepoRunner.Contracts.Events.RunMode.Dockerfile;

        // Produce RunRequested event - Orchestrator will create the Run record
        await _runProducer.PublishAsync(new RunRequested
        {
            RunId = runId,
            RepoUrl = request.RepoUrl,
            Branch = request.Branch,
            Mode = eventMode,
            ComposePath = request.ComposePath,
            PrimaryService = request.PrimaryService
        });

        _logger.LogInformation("Produced RunRequested event for runId: {RunId}", runId);

        return new StartRunResponse
        {
            RunId = runId,
            Status = RunStatus.Queued,
            Mode = request.Mode,
            PrimaryService = request.PrimaryService
        };
    }

    public override async Task<Empty> StopRun(StopRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StopRun called for runId: {RunId}", request.RunId);

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

        return new Empty();
    }

    public override async Task<RunStatusResponse> GetRunStatus(GetRunStatusRequest request, ServerCallContext context)
    {
        // Try Redis cache first (fast path)
        if (_statusCache != null)
        {
            try
            {
                var cachedStatus = await _statusCache.GetAsync(request.RunId, context?.CancellationToken ?? CancellationToken.None);
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
                        PrimaryService = cachedStatus.PrimaryService ?? "",
                        BuildProgress = cachedStatus.BuildProgress ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get status from cache for runId: {RunId}, cache service may not be initialized", request.RunId);
            }
        }
        else
        {
            _logger.LogWarning("Cache service is null for runId: {RunId}, this indicates DI construction failure", request.RunId);
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
}
