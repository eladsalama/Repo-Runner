using RepoRunner.Contracts.Events;

namespace Orchestrator.Models;

public class Run
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RepoUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public RunMode Mode { get; set; } = RunMode.Dockerfile;
    public string? ComposePath { get; set; }
    public string? PrimaryService { get; set; }
    public string Status { get; set; } = "Queued"; // Queued, Building, Running, Succeeded, Failed, Stopped
    public string? ImageRef { get; set; }
    public List<string> ImageRefs { get; set; } = new(); // For COMPOSE mode
    public List<int> Ports { get; set; } = new();
    public string? PreviewUrl { get; set; }
    public string? NamespaceName { get; set; } // K8s namespace where the run is deployed
    public string? LogsRef { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
