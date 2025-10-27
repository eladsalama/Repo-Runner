using Shared.Streams;
using Shared.Repositories;
using Shared.Models;
using RepoRunner.Contracts.Events;
using Google.Protobuf.WellKnownTypes;

namespace Builder.Services;

public class BuilderWorker : BackgroundService
{
    private readonly ILogger<BuilderWorker> _logger;
    private readonly IStreamConsumer<RunRequested> _consumer;
    private readonly IStreamProducer<BuildSucceeded> _buildSucceededProducer;
    private readonly IStreamProducer<BuildFailed> _buildFailedProducer;
    private readonly IStreamProducer<BuildProgress> _buildProgressProducer;
    private readonly IGitCloner _gitCloner;
    private readonly IDockerBuilder _dockerBuilder;
    private readonly IBuildLogsRepository _buildLogsRepository;
    private readonly ILogRepository _logRepository;
    private readonly IConfiguration _configuration;

    public BuilderWorker(
        ILogger<BuilderWorker> logger,
        IStreamConsumer<RunRequested> consumer,
        IStreamProducer<BuildSucceeded> buildSucceededProducer,
        IStreamProducer<BuildFailed> buildFailedProducer,
        IStreamProducer<BuildProgress> buildProgressProducer,
        IGitCloner gitCloner,
        IDockerBuilder dockerBuilder,
        IBuildLogsRepository buildLogsRepository,
        ILogRepository logRepository,
        IConfiguration configuration)
    {
        _logger = logger;
        _consumer = consumer;
        _buildSucceededProducer = buildSucceededProducer;
        _buildFailedProducer = buildFailedProducer;
        _buildProgressProducer = buildProgressProducer;
        _gitCloner = gitCloner;
        _dockerBuilder = dockerBuilder;
        _buildLogsRepository = buildLogsRepository;
        _logRepository = logRepository;
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
                await WriteLogAsync(runRequested.RunId, $"Successfully cloned {runRequested.RepoUrl}", null, stoppingToken);

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
        await WriteLogAsync(runRequested.RunId, $"Found Dockerfile at {dockerfilePath}", null, cancellationToken);

        // Emit progress: Building image
        await EmitBuildProgressAsync(runRequested.RunId, "Building image", cancellationToken);

        // Build Docker image
        var image = await _dockerBuilder.BuildDockerfileAsync(
            runRequested.RunId,
            repoPath,
            dockerfilePath,
            cancellationToken);

        buildLog.Content += $"Built image: {image.ImageRef}\n";
        buildLog.Content += $"Detected ports: {string.Join(", ", image.Ports)}\n";

        // Emit progress: Loading into kind
        await EmitBuildProgressAsync(runRequested.RunId, "Loading image into kind cluster", cancellationToken);

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

        // Track total services for progress
        var serviceCount = 0;
        var currentService = 0;

        // Build compose services with progress reporting
        var services = await _dockerBuilder.BuildComposeAsync(
            runRequested.RunId,
            repoPath,
            composePath,
            async (current, total, serviceName) =>
            {
                serviceCount = total;
                currentService = current;
                
                // Emit BuildProgress event for service builds
                var progress = new BuildProgress
                {
                    RunId = runRequested.RunId,
                    Current = current,
                    Total = total + 1, // +1 for the "loading into kind" step
                    ServiceName = $"Building {serviceName}",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _buildProgressProducer.PublishAsync(progress, cancellationToken);
                _logger.LogInformation(
                    "📊 Progress: [{Current}/{Total}] - Building {ServiceName}",
                    current, total + 1, serviceName);
            },
            cancellationToken);

        buildLog.Content += $"Processed {services.Count} services:\n";
        
        // Count services that need to be loaded into kind
        var servicesToLoad = services.Where(s => s.HasBuildContext).ToList();
        
        if (servicesToLoad.Any())
        {
            // Emit progress for each image being loaded
            int loadedCount = 0;
            foreach (var service in servicesToLoad)
            {
                loadedCount++;
                
                // Emit progress: Loading image [X/Y]
                var loadProgress = new BuildProgress
                {
                    RunId = runRequested.RunId,
                    Current = serviceCount + loadedCount,
                    Total = serviceCount + servicesToLoad.Count,
                    ServiceName = $"Loading {service.Name} into kind cluster",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _buildProgressProducer.PublishAsync(loadProgress, cancellationToken);
                
                buildLog.Content += $"  - {service.Name}: {service.ImageRef} (ports: {string.Join(", ", service.Ports)})\n";
                await _dockerBuilder.LoadIntoKindAsync(service.ImageRef, cancellationToken);
                buildLog.Content += $"    Loaded {service.ImageRef} into kind cluster\n";
            }
        }
        else
        {
            // All services are image-only (external images like postgres, redis)
            _logger.LogInformation(
                "All {Count} services use external images (no custom builds required)",
                services.Count);
            
            // Emit progress showing we're using external images
            var externalProgress = new BuildProgress
            {
                RunId = runRequested.RunId,
                Current = 1,
                Total = 1,
                ServiceName = $"Using {services.Count} external image(s) (postgres, redis, etc.)",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await _buildProgressProducer.PublishAsync(externalProgress, cancellationToken);
            
            foreach (var service in services)
            {
                buildLog.Content += $"  - {service.Name}: {service.ImageRef} (external image, ports: {string.Join(", ", service.Ports)})\n";
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
            foreach (var envKvp in service.Environment)
            {
                serviceInfo.Environment.Add(envKvp.Key, envKvp.Value);
            }
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
                await Task.Run(() =>
                {
                    // Remove read-only attributes from all files (git creates read-only files in .git/objects)
                    SetAttributesNormal(new DirectoryInfo(repoPath));
                    Directory.Delete(repoPath, recursive: true);
                });
                _logger.LogInformation("Cleaned up repository at {Path}", repoPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup repository at {Path} - will continue anyway", repoPath);
        }
    }

    /// <summary>
    /// Recursively remove read-only attributes from files and directories
    /// </summary>
    private void SetAttributesNormal(DirectoryInfo dir)
    {
        try
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }

            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
            
            dir.Attributes = FileAttributes.Normal;
        }
        catch
        {
            // Ignore errors - best effort cleanup
        }
    }

    /// <summary>
    /// Helper to write individual log entries for real-time streaming
    /// </summary>
    private async Task WriteLogAsync(string runId, string line, string? serviceName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var logEntry = new Shared.Models.LogEntry
            {
                RunId = runId,
                Source = "build",
                ServiceName = serviceName,
                Line = line,
                Timestamp = DateTime.UtcNow
            };
            await _logRepository.AddLogAsync(logEntry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write log entry for RunId={RunId}", runId);
        }
    }

    /// <summary>
    /// Emit build progress updates for better UX
    /// </summary>
    private async Task EmitBuildProgressAsync(string runId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var progress = new BuildProgress
            {
                RunId = runId,
                Current = 0,
                Total = 0,
                ServiceName = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await _buildProgressProducer.PublishAsync(progress, cancellationToken);
            _logger.LogInformation("📊 Progress: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit build progress for RunId={RunId}", runId);
        }
    }
}
