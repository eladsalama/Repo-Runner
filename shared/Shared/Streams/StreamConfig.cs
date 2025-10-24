namespace Shared.Streams;

/// <summary>
/// Configuration for Redis Streams
/// </summary>
public class StreamConfig
{
    /// <summary>
    /// Stream name constants
    /// </summary>
    public static class Streams
    {
        public const string RepoRuns = "stream:repo-runs";
        public const string Indexing = "stream:indexing";
    }

    /// <summary>
    /// Consumer group name constants
    /// </summary>
    public static class Groups
    {
        public const string Orchestrator = "group:orchestrator";
        public const string Builder = "group:builder";
        public const string Runner = "group:runner";
        public const string Indexer = "group:indexer";
    }

    /// <summary>
    /// Dead Letter Queue key
    /// </summary>
    public const string DeadLetterQueue = "list:dlq";

    /// <summary>
    /// Maximum retry attempts before sending to DLQ
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Idle time before message is considered stale and can be claimed (ms)
    /// </summary>
    public const int IdleTimeoutMs = 60000; // 1 minute
}
