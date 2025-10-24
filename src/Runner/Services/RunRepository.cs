using MongoDB.Driver;

namespace Runner.Services;

public class RunRepository : IRunRepository
{
    private readonly IMongoCollection<Run> _collection;
    private readonly ILogger<RunRepository> _logger;

    public RunRepository(IMongoDatabase database, ILogger<RunRepository> logger)
    {
        _collection = database.GetCollection<Run>("runs");
        _logger = logger;
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
}
