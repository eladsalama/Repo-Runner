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

    public async Task<DockerImage> BuildDockerfileAsync(
        string runId,
        string repoPath,
        string dockerfilePath,
        CancellationToken cancellationToken = default)
    {
        var imageTag = $"{runId}:latest";
        var dockerfileFullPath = Path.Combine(repoPath, dockerfilePath);
        var contextPath = Path.GetDirectoryName(dockerfileFullPath) ?? repoPath;

        _logger.LogInformation(
            "Building Docker image {ImageTag} from {Dockerfile} with context {Context}",
            imageTag, dockerfilePath, contextPath);

        var buildLogs = new StringBuilder();

        try
        {
            // Build with buildx
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"buildx build --tag {imageTag} --file \"{dockerfileFullPath}\" \"{contextPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = contextPath
            };

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
        CancellationToken cancellationToken = default)
    {
        var composeFullPath = Path.Combine(repoPath, composePath);
        _logger.LogInformation("Building docker-compose services from {ComposePath}", composeFullPath);

        // Parse compose file
        var services = await _composeParser.ParseAsync(composeFullPath, cancellationToken);
        var result = new List<DockerComposeService>();

        foreach (var service in services)
        {
            if (!service.HasBuildContext)
            {
                _logger.LogInformation(
                    "Skipping service {Service} - no build context (image-only service)",
                    service.Name);
                
                // Still include image-only services in result
                result.Add(new DockerComposeService
                {
                    Name = service.Name,
                    ImageRef = service.ImageRef ?? string.Empty,
                    Ports = service.Ports,
                    HasBuildContext = false
                });
                continue;
            }

            // Build the service
            var imageTag = $"{runId}-{service.Name}:latest";
            _logger.LogInformation("Building service {Service} as {ImageTag}", service.Name, imageTag);

            var buildLogs = new StringBuilder();
            try
            {
                var contextPath = Path.Combine(repoPath, service.BuildContext ?? ".");
                var dockerfileArg = !string.IsNullOrEmpty(service.Dockerfile) 
                    ? $"--file \"{Path.Combine(contextPath, service.Dockerfile)}\"" 
                    : "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"buildx build --tag {imageTag} {dockerfileArg} \"{contextPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = repoPath
                };

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
                            _logger.LogDebug("[{Service}] {Line}", service.Name, line);
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
                            _logger.LogDebug("[{Service}] {Line}", service.Name, line);
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
                    HasBuildContext = true
                });

                _logger.LogInformation(
                    "Successfully built service {Service} as {ImageTag} with ports {Ports}",
                    service.Name, imageTag, string.Join(", ", service.Ports));
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
            Arguments = $"load docker-image {imageName}",
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
