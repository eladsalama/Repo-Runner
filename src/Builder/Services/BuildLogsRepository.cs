using MongoDB.Driver;

namespace Builder.Services;

public class BuildLogsRepository : IBuildLogsRepository
{
    private readonly IMongoCollection<BuildLog> _collection;
    private readonly ILogger<BuildLogsRepository> _logger;

    public BuildLogsRepository(IMongoDatabase database, ILogger<BuildLogsRepository> logger)
    {
        _collection = database.GetCollection<BuildLog>("build_logs");
        _logger = logger;
        
        // Create index on RunId for fast lookups
        var indexKeysDefinition = Builders<BuildLog>.IndexKeys.Ascending(x => x.RunId);
        var indexModel = new CreateIndexModel<BuildLog>(indexKeysDefinition);
        _collection.Indexes.CreateOneAsync(indexModel).Wait();
    }

    public async Task SaveAsync(BuildLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            await _collection.InsertOneAsync(log, cancellationToken: cancellationToken);
            _logger.LogInformation("Saved build log for RunId: {RunId}", log.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save build log for RunId: {RunId}", log.RunId);
            throw;
        }
    }

    public async Task<BuildLog?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _collection.Find(x => x.RunId == runId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve build log for RunId: {RunId}", runId);
            throw;
        }
    }
}
