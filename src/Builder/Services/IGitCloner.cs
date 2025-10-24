namespace Builder.Services;

public interface IGitCloner
{
    /// <summary>
    /// Shallow clone a Git repository
    /// </summary>
    /// <param name="repoUrl">Repository URL</param>
    /// <param name="branch">Branch to clone (default: main/master)</param>
    /// <param name="workDirectory">Base work directory</param>
    /// <returns>Path to cloned repository</returns>
    Task<string> CloneAsync(string repoUrl, string branch, string workDirectory, CancellationToken cancellationToken = default);
}
