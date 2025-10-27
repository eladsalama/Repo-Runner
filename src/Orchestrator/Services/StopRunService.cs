using Shared.Streams;
using Shared.Repositories;
using RepoRunner.Contracts.Events;
using Google.Protobuf.WellKnownTypes;

namespace Orchestrator.Services;

/// <summary>
/// Service to handle StopRun requests by producing RunStopRequested events
/// </summary>
public interface IStopRunService
{
    Task StopRunAsync(string runId, CancellationToken cancellationToken = default);
}

public class StopRunService : IStopRunService
{
    private readonly ILogger<StopRunService> _logger;
    private readonly IStreamProducer<RunStopRequested> _stopProducer;
    private readonly IRunRepository _runRepository;

    public StopRunService(
        ILogger<StopRunService> logger,
        IStreamProducer<RunStopRequested> stopProducer,
        IRunRepository runRepository)
    {
        _logger = logger;
        _stopProducer = stopProducer;
        _runRepository = runRepository;
    }

    public async Task StopRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing StopRun request for RunId={RunId}", runId);

        // Get the run to retrieve namespace info
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run == null)
        {
            _logger.LogWarning("Run not found: {RunId}", runId);
            throw new InvalidOperationException($"Run {runId} not found");
        }

        // Update run status to Stopped
        run.Status = "Stopped";
        run.CompletedAt = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);

        // Produce RunStopRequested event for Runner to consume
        var stopEvent = new RunStopRequested
        {
            RunId = runId,
            Namespace = run.NamespaceName ?? $"run-{runId}",
            RequestedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await _stopProducer.PublishAsync(stopEvent, cancellationToken);
        _logger.LogInformation("Produced RunStopRequested event for RunId={RunId}, Namespace={Namespace}",
            runId, stopEvent.Namespace);
    }
}
