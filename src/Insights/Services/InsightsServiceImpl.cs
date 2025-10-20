using Grpc.Core;
using RepoRunner.Contracts;

namespace Insights.Services;

public class InsightsServiceImpl : InsightsService.InsightsServiceBase
{
    private readonly ILogger<InsightsServiceImpl> _logger;

    public InsightsServiceImpl(ILogger<InsightsServiceImpl> logger)
    {
        _logger = logger;
    }

    public override Task<AskRepoResponse> AskRepo(AskRepoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("AskRepo called for repo: {RepoId}, question: {Question}", 
            request.RepoId, request.Question);
        
        throw new RpcException(new Status(StatusCode.Unimplemented, "Not yet implemented"));
    }
}
