using Microsoft.AspNetCore.Mvc;
using RepoRunner.Contracts;
using Gateway.Services;

namespace Gateway.Controllers;

/// <summary>
/// REST API controller for MVP skeleton
/// Provides HTTP endpoints that forward to internal gRPC services
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly RunServiceImpl _runService;
    private readonly ILogger<RunsController> _logger;

    public RunsController(RunServiceImpl runService, ILogger<RunsController> logger)
    {
        _runService = runService;
        _logger = logger;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartRun([FromBody] StartRunDto request)
    {
        try
        {
            // Map RunMode from string to enum
            var mode = request.Mode?.ToUpperInvariant() switch
            {
                "COMPOSE" => RunMode.Compose,
                "DOCKERFILE" => RunMode.Dockerfile,
                _ => RunMode.Dockerfile
            };

            var grpcRequest = new StartRunRequest
            {
                RepoUrl = request.RepoUrl,
                Branch = request.Branch ?? "main",
                Mode = mode,
                ComposePath = request.ComposePath ?? "",
                PrimaryService = request.PrimaryService ?? ""
            };

            var response = await _runService.StartRun(grpcRequest, null!);

            return Ok(new
            {
                runId = response.RunId,
                status = response.Status.ToString(),
                mode = response.Mode.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting run");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{runId}/stop")]
    public async Task<IActionResult> StopRun(string runId)
    {
        try
        {
            await _runService.StopRun(new StopRunRequest { RunId = runId }, null!);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping run");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{runId}/status")]
    public async Task<IActionResult> GetRunStatus(string runId)
    {
        try
        {
            var response = await _runService.GetRunStatus(new GetRunStatusRequest { RunId = runId }, null!);

            return Ok(new
            {
                runId = response.RunId,
                status = response.Status.ToString(),
                previewUrl = response.PreviewUrl,
                startedAt = response.StartedAt?.ToDateTime(),
                endedAt = response.EndedAt?.ToDateTime(),
                errorMessage = response.ErrorMessage,
                mode = response.Mode.ToString(),
                primaryService = response.PrimaryService
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting run status");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// DTO for StartRun REST endpoint
/// </summary>
public class StartRunDto
{
    public string RepoUrl { get; set; } = "";
    public string? Branch { get; set; }
    public string? Mode { get; set; }
    public string? ComposePath { get; set; }
    public string? PrimaryService { get; set; }
}
