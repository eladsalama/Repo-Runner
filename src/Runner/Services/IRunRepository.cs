using RepoRunner.Contracts.Events;

namespace Runner.Services;

public interface IRunRepository
{
    Task<Run?> GetByIdAsync(string runId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Run run, CancellationToken cancellationToken = default);
}

public class Run
{
    public string Id { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public RunMode Mode { get; set; } = RunMode.Dockerfile;
    public string? ComposePath { get; set; }
    public string? PrimaryService { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ImageRef { get; set; }
    public List<string> ImageRefs { get; set; } = new();
    public List<int> Ports { get; set; } = new();
    public string? PreviewUrl { get; set; }
    public string? NamespaceName { get; set; }
    public string? LogsRef { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
