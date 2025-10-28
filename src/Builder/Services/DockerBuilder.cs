using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Builder.Services;

public class DockerBuilder : IDockerBuilder
{
    private readonly ILogger<DockerBuilder> _logger;
    private readonly IDockerComposeParser _composeParser;
    private readonly IConfiguration _configuration;

    public DockerBuilder(
        ILogger<DockerBuilder> logger,
        IDockerComposeParser composeParser,
        IConfiguration configuration)
    {
        _logger = logger;
        _composeParser = composeParser;
        _configuration = configuration;
    }

    /// <summary>
    /// Calculate optimal CPU allocation for builds based on system resources
    /// Uses 60% of available cores (min 2, max cores-2) to keep system responsive
    /// Also checks system memory to ensure we don't overload
    /// </summary>
    private (int cpus, string parallelism) GetOptimalBuildResources()
    {
        try
        {
            var totalCpus = Environment.ProcessorCount;
            
            // Calculate optimal CPUs: Use 60% of cores
            // Min: 2 cores (even on low-end systems)
            // Max: total-2 (always leave 2 cores for system)
            var optimalCpus = Math.Max(2, Math.Min(totalCpus - 2, (int)Math.Ceiling(totalCpus * 0.6)));
            
            // For systems with many cores (12+), we can be more aggressive
            if (totalCpus >= 12)
            {
                optimalCpus = Math.Max(6, totalCpus - 4); // Leave 4 cores for system on high-end machines
            }
            
            // Set BuildKit parallelism (number of parallel build steps)
            var parallelism = Math.Max(2, optimalCpus / 2).ToString();
            
            _logger.LogInformation(
                "System has {Total} CPU cores, allocating {Build} cores for build (parallelism: {Parallel})",
                totalCpus, optimalCpus, parallelism);
            
            return (optimalCpus, parallelism);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect CPU count, using defaults");
            return (4, "2"); // Fallback
        }
    }

    public async Task<DockerImage> BuildDockerfileAsync(
        string runId,
        string repoPath,
        string dockerfilePath,
        CancellationToken cancellationToken = default)
    {
        var imageTag = $"{runId}:latest";
        var dockerfileFullPath = Path.Combine(repoPath, dockerfilePath);
        var contextPath = Path.GetDirectoryName(dockerfileFullPath) ?? repoPath;

        // Convert to absolute path if relative, then to Unix-style for Docker (Docker on Windows uses WSL2)
        var absoluteContextPath = Path.GetFullPath(contextPath);
        var absoluteDockerfilePath = Path.GetFullPath(dockerfileFullPath);
        var dockerContextPath = absoluteContextPath.Replace("\\", "/");
        var dockerDockerfilePath = absoluteDockerfilePath.Replace("\\", "/");

        _logger.LogInformation(
            "Building Docker image {ImageTag} from {Dockerfile} with context {Context}",
            imageTag, dockerfilePath, dockerContextPath);

        var buildLogs = new StringBuilder();

        try
        {
            // Build with buildx and dynamic CPU allocation via BuildKit env vars
            var (cpus, parallelism) = GetOptimalBuildResources();
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"buildx build --tag {imageTag} --file \"{dockerDockerfilePath}\" \"{dockerContextPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = absoluteContextPath
            };

            // Set BuildKit environment variables for parallelism
            startInfo.Environment["BUILDKIT_STEP_LOG_MAX_SIZE"] = "10485760"; // 10MB
            startInfo.Environment["BUILDKIT_STEP_LOG_MAX_SPEED"] = "10485760"; // 10MB/s
            startInfo.EnvironmentVariables["GOMAXPROCS"] = cpus.ToString(); // Limit Go runtime CPUs

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start docker process");
            }

            // Capture output
            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        buildLogs.AppendLine(line);
                        _logger.LogDebug("Build: {Line}", line);
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        buildLogs.AppendLine(line);
                        _logger.LogDebug("Build: {Line}", line);
                    }
                }
            }, cancellationToken);

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Docker build failed with exit code {process.ExitCode}:\n{buildLogs}");
            }

            // Detect ports from Dockerfile
            var ports = await DetectPortsFromDockerfileAsync(dockerfileFullPath, cancellationToken);

            _logger.LogInformation("Successfully built image {ImageTag} with ports {Ports}", imageTag, string.Join(", ", ports));

            return new DockerImage
            {
                ImageRef = imageTag,
                Ports = ports
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build Docker image: {Error}\nBuild logs:\n{Logs}", ex.Message, buildLogs);
            throw;
        }
    }

    public async Task<List<DockerComposeService>> BuildComposeAsync(
        string runId,
        string repoPath,
        string composePath,
        Func<int, int, string, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var composeFullPath = Path.Combine(repoPath, composePath);
        _logger.LogInformation("Building docker-compose services from {ComposePath}", composeFullPath);

        // Parse compose file
        var services = await _composeParser.ParseAsync(composeFullPath, cancellationToken);
        var result = new List<DockerComposeService>();
        
        var totalServices = services.Count;
        var currentService = 0;

        foreach (var service in services)
        {
            currentService++;
            
            if (!service.HasBuildContext)
            {
                _logger.LogInformation(
                    "[{Current}/{Total}] Skipping service {Service} - no build context (image-only service)",
                    currentService, totalServices, service.Name);
                
                // Still include image-only services in result
                result.Add(new DockerComposeService
                {
                    Name = service.Name,
                    ImageRef = service.ImageRef ?? string.Empty,
                    Ports = service.Ports,
                    HasBuildContext = false,
                    Environment = service.Environment
                });
                continue;
            }

            // Build the service
            var imageTag = $"{runId}-{service.Name}:latest";
            _logger.LogInformation("[{Current}/{Total}] Building service {Service} as {ImageTag}...", 
                currentService, totalServices, service.Name, imageTag);

            var buildLogs = new StringBuilder();
            try
            {
                // Handle build context path - use absolute path for Docker
                var buildContextRelative = service.BuildContext ?? ".";
                // Remove trailing slashes to avoid path issues
                buildContextRelative = buildContextRelative.TrimEnd('/', '\\');
                
                var contextPath = buildContextRelative == "." 
                    ? Path.GetFullPath(repoPath) 
                    : Path.GetFullPath(Path.Combine(repoPath, buildContextRelative));
                
                // Ensure no trailing slashes in the final path
                contextPath = contextPath.TrimEnd('\\');
                    
                var dockerfileArg = !string.IsNullOrEmpty(service.Dockerfile) 
                    ? $"--file \"{Path.Combine(contextPath, service.Dockerfile)}\"" 
                    : "";

                var (cpus, parallelism) = GetOptimalBuildResources();
                var startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"buildx build --tag {imageTag} --cache-from type=local,src=/tmp/.buildx-cache --cache-to type=local,dest=/tmp/.buildx-cache --build-arg BUILDKIT_INLINE_CACHE=1 {dockerfileArg} \"{contextPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Set BuildKit environment variables for parallelism
                startInfo.Environment["BUILDKIT_STEP_LOG_MAX_SIZE"] = "10485760";
                startInfo.Environment["BUILDKIT_STEP_LOG_MAX_SPEED"] = "10485760";
                startInfo.EnvironmentVariables["GOMAXPROCS"] = cpus.ToString();

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException($"Failed to start docker process for service {service.Name}");
                }

                // Capture output
                var outputTask = Task.Run(async () =>
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                        if (line != null)
                        {
                            buildLogs.AppendLine(line);
                            // Log important progress steps at Info level
                            if (line.Contains("CACHED") || line.Contains("DONE") || line.Contains("exporting") || 
                                line.Contains("writing image") || line.Contains("[stage-"))
                            {
                                _logger.LogInformation("[{Service}] {Line}", service.Name, line);
                            }
                            else
                            {
                                _logger.LogDebug("[{Service}] {Line}", service.Name, line);
                            }
                        }
                    }
                }, cancellationToken);

                var errorTask = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync(cancellationToken);
                        if (line != null)
                        {
                            buildLogs.AppendLine(line);
                            // Show progress indicators
                            if (line.Contains("#") || line.Contains("=>"))
                            {
                                _logger.LogInformation("[{Service}] {Line}", service.Name, line);
                            }
                            else
                            {
                                _logger.LogDebug("[{Service}] {Line}", service.Name, line);
                            }
                        }
                    }
                }, cancellationToken);

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Docker build failed for service {service.Name} with exit code {process.ExitCode}:\n{buildLogs}");
                }

                result.Add(new DockerComposeService
                {
                    Name = service.Name,
                    ImageRef = imageTag,
                    Ports = service.Ports,
                    HasBuildContext = true,
                    Environment = service.Environment
                });

                _logger.LogInformation(
                    "[{Current}/{Total}] ✅ Successfully built service {Service} with ports {Ports}",
                    currentService, totalServices, service.Name, string.Join(", ", service.Ports));
                
                // Report progress
                if (onProgress != null)
                {
                    await onProgress(currentService, totalServices, service.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build service {Service}: {Error}", service.Name, ex.Message);
                throw;
            }
        }

        return result;
    }

    public async Task LoadIntoKindAsync(string imageName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading image {ImageName} into kind cluster", imageName);

        var startInfo = new ProcessStartInfo
        {
            FileName = "kind",
            Arguments = $"load docker-image {imageName} --name reporunner",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start kind process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"kind load failed: {error}");
        }

        _logger.LogInformation("Successfully loaded image {ImageName} into kind", imageName);
    }

    private async Task<List<int>> DetectPortsFromDockerfileAsync(string dockerfilePath, CancellationToken cancellationToken)
    {
        var ports = new List<int>();
        
        try
        {
            var lines = await File.ReadAllLinesAsync(dockerfilePath, cancellationToken);
            var exposeRegex = new Regex(@"^\s*EXPOSE\s+(\d+)", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var match = exposeRegex.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                {
                    ports.Add(port);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect ports from Dockerfile {Path}", dockerfilePath);
        }

        // If no ports detected, assume common HTTP port
        if (ports.Count == 0)
        {
            _logger.LogInformation("No EXPOSE directives found, assuming port 8080");
            ports.Add(8080);
        }

        return ports;
    }
}
