namespace Builder.Services;

public class DockerImage
{
    public string ImageRef { get; set; } = string.Empty;
    public List<int> Ports { get; set; } = new();
}

public class DockerComposeService
{
    public string Name { get; set; } = string.Empty;
    public string ImageRef { get; set; } = string.Empty;
    public List<int> Ports { get; set; } = new();
    public bool HasBuildContext { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
}

public interface IDockerBuilder
{
    /// <summary>
    /// Build a Docker image from a Dockerfile
    /// </summary>
    Task<DockerImage> BuildDockerfileAsync(
        string runId, 
        string repoPath, 
        string dockerfilePath, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build Docker images for services defined in docker-compose.yml
    /// Optional onProgress callback receives (current, total, serviceName) after each service completes
    /// </summary>
    Task<List<DockerComposeService>> BuildComposeAsync(
        string runId,
        string repoPath,
        string composePath,
        Func<int, int, string, Task>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load image into kind cluster
    /// </summary>
    Task LoadIntoKindAsync(string imageName, CancellationToken cancellationToken = default);
}
