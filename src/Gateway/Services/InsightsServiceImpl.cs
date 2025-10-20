using Grpc.Core;
using RepoRunner.Contracts;

namespace Gateway.Services;

public class InsightsServiceImpl : InsightsService.InsightsServiceBase
{
    private readonly ILogger<InsightsServiceImpl> _logger;

    public InsightsServiceImpl(ILogger<InsightsServiceImpl> logger)
    {
        _logger = logger;
    }

    public override Task<AskRepoResponse> AskRepo(AskRepoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("AskRepo called for repoId: {RepoId}, question: {Question}", 
            request.RepoId, request.Question);
        
        // TODO: Implement - forward to Insights service for RAG
        throw new RpcException(new Status(StatusCode.Unimplemented, "AskRepo not yet implemented"));
    }
}
