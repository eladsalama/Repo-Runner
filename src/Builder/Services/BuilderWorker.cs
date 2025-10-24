using Shared.Streams;
using RepoRunner.Contracts.Events;
using Google.Protobuf.WellKnownTypes;

namespace Builder.Services;

public class BuilderWorker : BackgroundService
{
    private readonly ILogger<BuilderWorker> _logger;
    private readonly IStreamConsumer<RunRequested> _consumer;
    private readonly IStreamProducer<BuildSucceeded> _buildSucceededProducer;
    private readonly IStreamProducer<BuildFailed> _buildFailedProducer;
    private readonly IGitCloner _gitCloner;
    private readonly IDockerBuilder _dockerBuilder;
    private readonly IBuildLogsRepository _buildLogsRepository;
    private readonly IConfiguration _configuration;

    public BuilderWorker(
        ILogger<BuilderWorker> logger,
        IStreamConsumer<RunRequested> consumer,
        IStreamProducer<BuildSucceeded> buildSucceededProducer,
        IStreamProducer<BuildFailed> buildFailedProducer,
        IGitCloner gitCloner,
        IDockerBuilder dockerBuilder,
        IBuildLogsRepository buildLogsRepository,
        IConfiguration configuration)
    {
        _logger = logger;
        _consumer = consumer;
        _buildSucceededProducer = buildSucceededProducer;
        _buildFailedProducer = buildFailedProducer;
        _gitCloner = gitCloner;
        _dockerBuilder = dockerBuilder;
        _buildLogsRepository = buildLogsRepository;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Builder worker starting");

        var workDirectory = _configuration.GetValue<string>("Builder:WorkDirectory") ?? "./work";
        Directory.CreateDirectory(workDirectory);

        // Start consuming RunRequested events
        await _consumer.ConsumeAsync(async (runRequested) =>
        {
            _logger.LogInformation(
                "Received RunRequested event: RunId={RunId}, RepoUrl={RepoUrl}, Mode={Mode}",
                runRequested.RunId, runRequested.RepoUrl, runRequested.Mode);

            var buildLog = new BuildLog
            {
                RunId = runRequested.RunId,
                Status = "building"
            };

            try
            {
                // Clone repository
                var branch = string.IsNullOrEmpty(runRequested.Branch) ? "main" : runRequested.Branch;
                string repoPath;
                
                try
                {
                    repoPath = await _gitCloner.CloneAsync(
                        runRequested.RepoUrl,
                        branch,
                        workDirectory,
                        stoppingToken);
                }
                catch (Exception)
                {
                    // Try master branch if main fails
                    _logger.LogInformation("Failed to clone branch {Branch}, trying 'master'", branch);
                    repoPath = await _gitCloner.CloneAsync(
                        runRequested.RepoUrl,
                        "master",
                        workDirectory,
                        stoppingToken);
                }

                buildLog.Content = $"Successfully cloned {runRequested.RepoUrl}\n";

                // Build based on mode
                if (runRequested.Mode == RunMode.Dockerfile)
                {
                    await BuildDockerfileModeAsync(runRequested, repoPath, buildLog, stoppingToken);
                }
                else if (runRequested.Mode == RunMode.Compose)
                {
                    await BuildComposeModeAsync(runRequested, repoPath, buildLog, stoppingToken);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown run mode: {runRequested.Mode}");
                }

                // Cleanup
                await CleanupRepoAsync(repoPath);

                return true; // Acknowledge
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Build failed for RunId={RunId}: {Error}", runRequested.RunId, ex.Message);

                buildLog.Status = "failed";
                buildLog.Content += $"\n\nERROR: {ex.Message}\n{ex.StackTrace}";
                await _buildLogsRepository.SaveAsync(buildLog, stoppingToken);

                // Produce BuildFailed event
                var buildFailed = new BuildFailed
                {
                    RunId = runRequested.RunId,
                    Error = ex.Message,
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    LogsRef = buildLog.Id
                };

                // Add suggested fixes based on error
                if (ex.Message.Contains("not found") || ex.Message.Contains("does not exist"))
                {
                    buildFailed.SuggestedFixes.Add("Verify that the Dockerfile or docker-compose.yml exists in the repository root");
                }
                if (ex.Message.Contains("permission denied"))
                {
                    buildFailed.SuggestedFixes.Add("Check file permissions in the repository");
                }
                if (ex.Message.Contains("network") || ex.Message.Contains("timeout"))
                {
                    buildFailed.SuggestedFixes.Add("Check network connectivity and try again");
                }

                await _buildFailedProducer.PublishAsync(buildFailed, stoppingToken);

                return true; // Still acknowledge to prevent retry
            }
        }, stoppingToken);
    }

    private async Task BuildDockerfileModeAsync(
        RunRequested runRequested,
        string repoPath,
        BuildLog buildLog,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Building DOCKERFILE mode for RunId={RunId}", runRequested.RunId);

        // Detect Dockerfile location (root or common subdirs)
        var dockerfilePaths = new[] { "Dockerfile", "docker/Dockerfile", "build/Dockerfile", ".docker/Dockerfile" };
        string? dockerfilePath = null;

        foreach (var path in dockerfilePaths)
        {
            var fullPath = Path.Combine(repoPath, path);
            if (File.Exists(fullPath))
            {
                dockerfilePath = path;
                break;
            }
        }

        if (dockerfilePath == null)
        {
            throw new FileNotFoundException("Dockerfile not found in repository");
        }

        buildLog.Content += $"Found Dockerfile at {dockerfilePath}\n";

        // Build image
        var image = await _dockerBuilder.BuildDockerfileAsync(
            runRequested.RunId,
            repoPath,
            dockerfilePath,
            cancellationToken);

        buildLog.Content += $"Built image: {image.ImageRef}\n";
        buildLog.Content += $"Detected ports: {string.Join(", ", image.Ports)}\n";

        // Load into kind
        await _dockerBuilder.LoadIntoKindAsync(image.ImageRef, cancellationToken);
        buildLog.Content += $"Loaded image into kind cluster\n";

        buildLog.Status = "succeeded";
        await _buildLogsRepository.SaveAsync(buildLog, cancellationToken);

        // Produce BuildSucceeded event
        var buildSucceeded = new BuildSucceeded
        {
            RunId = runRequested.RunId,
            ImageRef = image.ImageRef,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            LogsRef = buildLog.Id,
            Mode = RunMode.Dockerfile
        };
        buildSucceeded.Ports.AddRange(image.Ports);

        await _buildSucceededProducer.PublishAsync(buildSucceeded, cancellationToken);

        _logger.LogInformation(
            "Successfully built DOCKERFILE mode for RunId={RunId}, Image={Image}",
            runRequested.RunId, image.ImageRef);
    }

    private async Task BuildComposeModeAsync(
        RunRequested runRequested,
        string repoPath,
        BuildLog buildLog,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Building COMPOSE mode for RunId={RunId}", runRequested.RunId);

        var composePath = string.IsNullOrEmpty(runRequested.ComposePath)
            ? "docker-compose.yml"
            : runRequested.ComposePath;

        var composeFullPath = Path.Combine(repoPath, composePath);
        if (!File.Exists(composeFullPath))
        {
            throw new FileNotFoundException($"docker-compose.yml not found at {composePath}");
        }

        buildLog.Content += $"Found docker-compose.yml at {composePath}\n";

        // Build compose services
        var services = await _dockerBuilder.BuildComposeAsync(
            runRequested.RunId,
            repoPath,
            composePath,
            cancellationToken);

        buildLog.Content += $"Built {services.Count} services:\n";
        foreach (var service in services)
        {
            buildLog.Content += $"  - {service.Name}: {service.ImageRef} (ports: {string.Join(", ", service.Ports)})\n";
            
            // Load built images into kind (skip image-only services)
            if (service.HasBuildContext)
            {
                await _dockerBuilder.LoadIntoKindAsync(service.ImageRef, cancellationToken);
                buildLog.Content += $"    Loaded {service.ImageRef} into kind cluster\n";
            }
        }

        buildLog.Status = "succeeded";
        await _buildLogsRepository.SaveAsync(buildLog, cancellationToken);

        // Produce BuildSucceeded event
        var buildSucceeded = new BuildSucceeded
        {
            RunId = runRequested.RunId,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            LogsRef = buildLog.Id,
            Mode = RunMode.Compose
        };

        // Add service info
        foreach (var service in services)
        {
            var serviceInfo = new ServiceInfo
            {
                Name = service.Name,
                ImageRef = service.ImageRef
            };
            serviceInfo.Ports.AddRange(service.Ports);
            buildSucceeded.Services.Add(serviceInfo);
        }

        await _buildSucceededProducer.PublishAsync(buildSucceeded, cancellationToken);

        _logger.LogInformation(
            "Successfully built COMPOSE mode for RunId={RunId}, Services={Count}",
            runRequested.RunId, services.Count);
    }

    private async Task CleanupRepoAsync(string repoPath)
    {
        try
        {
            if (Directory.Exists(repoPath))
            {
                await Task.Run(() => Directory.Delete(repoPath, recursive: true));
                _logger.LogInformation("Cleaned up repository at {Path}", repoPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup repository at {Path}", repoPath);
        }
    }
}
