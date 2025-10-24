using System.Diagnostics;

namespace Builder.Services;

public class GitCloner : IGitCloner
{
    private readonly ILogger<GitCloner> _logger;

    public GitCloner(ILogger<GitCloner> logger)
    {
        _logger = logger;
    }

    public async Task<string> CloneAsync(string repoUrl, string branch, string workDirectory, CancellationToken cancellationToken = default)
    {
        // Create work directory if it doesn't exist
        Directory.CreateDirectory(workDirectory);

        // Generate unique directory name for this clone
        var repoName = Path.GetFileNameWithoutExtension(repoUrl.TrimEnd('/').Split('/').Last());
        var clonePath = Path.Combine(workDirectory, $"{repoName}-{Guid.NewGuid():N}");

        _logger.LogInformation("Cloning {RepoUrl} (branch: {Branch}) to {Path}", repoUrl, branch, clonePath);

        try
        {
            // Shallow clone with depth 1
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --depth 1 --branch {branch} {repoUrl} \"{clonePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("Git clone failed: {Error}", error);
                throw new InvalidOperationException($"Git clone failed: {error}");
            }

            _logger.LogInformation("Successfully cloned repository to {Path}", clonePath);
            return clonePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning repository {RepoUrl}", repoUrl);
            
            // Cleanup on failure
            if (Directory.Exists(clonePath))
            {
                try
                {
                    Directory.Delete(clonePath, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup clone directory {Path}", clonePath);
                }
            }
            
            throw;
        }
    }
}
