using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Shared.Repositories;

public interface ILogRepository
{
    Task AddLogAsync(LogEntry logEntry, CancellationToken cancellationToken = default);
    Task AddLogsAsync(IEnumerable<LogEntry> logEntries, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LogEntry> GetLogsAsync(string runId, string? source = null, string? serviceName = null, CancellationToken cancellationToken = default);
}

public class LogRepository : ILogRepository
{
    private readonly IMongoCollection<LogEntry> _collection;
    private readonly ILogger<LogRepository> _logger;

    public LogRepository(IMongoDatabase database, ILogger<LogRepository> logger)
    {
        _collection = database.GetCollection<LogEntry>("logs");
        _logger = logger;

        // Create indexes
        var runIdIndex = Builders<LogEntry>.IndexKeys.Ascending(x => x.RunId);
        var timestampIndex = Builders<LogEntry>.IndexKeys.Ascending(x => x.Timestamp);
        var compoundIndex = Builders<LogEntry>.IndexKeys
            .Ascending(x => x.RunId)
            .Ascending(x => x.Source)
            .Ascending(x => x.ServiceName)
            .Ascending(x => x.Timestamp);

        try
        {
            _collection.Indexes.CreateOne(new CreateIndexModel<LogEntry>(runIdIndex));
            _collection.Indexes.CreateOne(new CreateIndexModel<LogEntry>(timestampIndex));
            _collection.Indexes.CreateOne(new CreateIndexModel<LogEntry>(compoundIndex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes on logs collection");
        }
    }

    public async Task AddLogAsync(LogEntry logEntry, CancellationToken cancellationToken = default)
    {
        await _collection.InsertOneAsync(logEntry, cancellationToken: cancellationToken);
    }

    public async Task AddLogsAsync(IEnumerable<LogEntry> logEntries, CancellationToken cancellationToken = default)
    {
        if (!logEntries.Any()) return;
        await _collection.InsertManyAsync(logEntries, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<LogEntry> GetLogsAsync(
        string runId,
        string? source = null,
        string? serviceName = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LogEntry>.Filter;
        var filter = filterBuilder.Eq(x => x.RunId, runId);

        if (!string.IsNullOrEmpty(source))
        {
            filter &= filterBuilder.Eq(x => x.Source, source);
        }

        if (!string.IsNullOrEmpty(serviceName))
        {
            filter &= filterBuilder.Eq(x => x.ServiceName, serviceName);
        }

        var options = new FindOptions<LogEntry>
        {
            Sort = Builders<LogEntry>.Sort.Ascending(x => x.Timestamp)
        };

        using var cursor = await _collection.FindAsync(filter, options, cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var log in cursor.Current)
            {
                yield return log;
            }
        }
    }
}
