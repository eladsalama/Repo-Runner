namespace Shared.Models;

/// <summary>
/// Log entry stored in MongoDB
/// </summary>
public class LogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "build" or "run"
    public string? ServiceName { get; set; } // For COMPOSE mode
    public string Line { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
