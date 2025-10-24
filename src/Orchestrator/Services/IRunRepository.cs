using Orchestrator.Models;

namespace Orchestrator.Services;

public interface IRunRepository
{
    Task<Run> CreateAsync(Run run, CancellationToken cancellationToken = default);
    Task<Run?> GetByIdAsync(string runId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Run run, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string runId, string status, CancellationToken cancellationToken = default);
}
