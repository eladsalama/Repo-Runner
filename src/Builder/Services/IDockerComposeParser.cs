namespace Builder.Services;

public class ComposeService
{
    public string Name { get; set; } = string.Empty;
    public string? ImageRef { get; set; }
    public string? BuildContext { get; set; }
    public string? Dockerfile { get; set; }
    public List<int> Ports { get; set; } = new();
    public bool HasBuildContext { get; set; }
}

public interface IDockerComposeParser
{
    /// <summary>
    /// Parse docker-compose.yml and extract service information
    /// </summary>
    Task<List<ComposeService>> ParseAsync(string composePath, CancellationToken cancellationToken = default);
}
