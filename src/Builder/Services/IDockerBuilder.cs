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
    /// </summary>
    Task<List<DockerComposeService>> BuildComposeAsync(
        string runId,
        string repoPath,
        string composePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load image into kind cluster
    /// </summary>
    Task LoadIntoKindAsync(string imageName, CancellationToken cancellationToken = default);
}
