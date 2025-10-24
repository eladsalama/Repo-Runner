using MongoDB.Driver;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class RunRepository : IRunRepository
{
    private readonly IMongoCollection<Run> _collection;
    private readonly ILogger<RunRepository> _logger;

    public RunRepository(IMongoDatabase database, ILogger<RunRepository> logger)
    {
        _collection = database.GetCollection<Run>("runs");
        _logger = logger;
        
        // Create indices
        var indexKeysDefinition = Builders<Run>.IndexKeys
            .Ascending(x => x.RepoUrl)
            .Descending(x => x.CreatedAt);
        var indexModel = new CreateIndexModel<Run>(indexKeysDefinition);
        _collection.Indexes.CreateOneAsync(indexModel).Wait();
    }

    public async Task<Run> CreateAsync(Run run, CancellationToken cancellationToken = default)
    {
        try
        {
            await _collection.InsertOneAsync(run, cancellationToken: cancellationToken);
            _logger.LogInformation("Created run: {RunId} for repo {RepoUrl}", run.Id, run.RepoUrl);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create run for repo {RepoUrl}", run.RepoUrl);
            throw;
        }
    }

    public async Task<Run?> GetByIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _collection.Find(x => x.Id == runId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve run {RunId}", runId);
            throw;
        }
    }

    public async Task UpdateAsync(Run run, CancellationToken cancellationToken = default)
    {
        try
        {
            await _collection.ReplaceOneAsync(
                x => x.Id == run.Id,
                run,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Updated run: {RunId}", run.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update run {RunId}", run.Id);
            throw;
        }
    }

    public async Task UpdateStatusAsync(string runId, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            var update = Builders<Run>.Update.Set(x => x.Status, status);
            await _collection.UpdateOneAsync(
                x => x.Id == runId,
                update,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Updated run {RunId} status to {Status}", runId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for run {RunId}", runId);
            throw;
        }
    }
}
