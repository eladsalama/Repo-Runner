namespace Builder.Services;

public class BuildLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = string.Empty;
    public string? ServiceName { get; set; } // For COMPOSE mode
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "building"; // building, succeeded, failed
}

public interface IBuildLogsRepository
{
    Task SaveAsync(BuildLog log, CancellationToken cancellationToken = default);
    Task<BuildLog?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default);
}
