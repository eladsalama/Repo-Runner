using Grpc.Core;
using RepoRunner.Contracts;

namespace Gateway.Services;

public class RunServiceImpl : RunService.RunServiceBase
{
    private readonly ILogger<RunServiceImpl> _logger;

    public RunServiceImpl(ILogger<RunServiceImpl> logger)
    {
        _logger = logger;
    }

    public override Task<StartRunResponse> StartRun(StartRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StartRun called for repo: {RepoUrl}", request.RepoUrl);
        
        // TODO: Implement - forward to Orchestrator
        throw new RpcException(new Status(StatusCode.Unimplemented, "StartRun not yet implemented"));
    }

    public override Task<Google.Protobuf.WellKnownTypes.Empty> StopRun(StopRunRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StopRun called for runId: {RunId}", request.RunId);
        
        // TODO: Implement - forward to Orchestrator
        throw new RpcException(new Status(StatusCode.Unimplemented, "StopRun not yet implemented"));
    }

    public override Task<RunStatusResponse> GetRunStatus(GetRunStatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetRunStatus called for runId: {RunId}", request.RunId);
        
        // TODO: Implement - check Redis cache first, fallback to Orchestrator
        throw new RpcException(new Status(StatusCode.Unimplemented, "GetRunStatus not yet implemented"));
    }

    public override Task StreamLogs(StreamLogsRequest request, IServerStreamWriter<LogEntry> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("StreamLogs called for runId: {RunId}", request.RunId);
        
        // TODO: Implement - stream logs from Mongo/Redis
        throw new RpcException(new Status(StatusCode.Unimplemented, "StreamLogs not yet implemented"));
    }
}
