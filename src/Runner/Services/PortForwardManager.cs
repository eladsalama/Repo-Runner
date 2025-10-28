using System.Diagnostics;
using System.Collections.Concurrent;

namespace Runner.Services;

/// <summary>
/// Manages kubectl port-forward processes for deployed applications.
/// Automatically creates port-forwards for services and tracks them for cleanup.
/// </summary>
public class PortForwardManager : IDisposable
{
    private readonly ILogger<PortForwardManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, PortForwardInfo> _portForwards = new();
    private bool _disposed;

    public PortForwardManager(ILogger<PortForwardManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Creates a port-forward to a specific pod and port.
    /// Returns the local URL that can be used to access the service.
    /// </summary>
    public async Task<string> CreatePortForwardAsync(
        string namespaceName,
        string serviceName,
        int targetPort,
        CancellationToken cancellationToken = default)
    {
        var key = $"{namespaceName}/{serviceName}";
        
        // Check if port-forward already exists and process is still alive
        if (_portForwards.TryGetValue(key, out var existing))
        {
            // Verify the process is still running
            if (existing.Process != null && !existing.Process.HasExited)
            {
                _logger.LogInformation(
                    "Port-forward already exists for {Service} in {Namespace} on port {Port}",
                    serviceName, namespaceName, existing.LocalPort);
                return existing.Url;
            }
            else
            {
                // Process died, remove from dictionary and recreate
                _logger.LogWarning(
                    "Port-forward process for {Service} in {Namespace} has died, recreating...",
                    serviceName, namespaceName);
                _portForwards.TryRemove(key, out _);
            }
        }

        // Find an available local port
        var localPort = await FindAvailablePortAsync(targetPort, cancellationToken);

        try
        {
            // Get pod name for the service
            var podName = await GetPodNameForServiceAsync(namespaceName, serviceName, cancellationToken);
            if (string.IsNullOrEmpty(podName))
            {
                _logger.LogWarning(
                    "No pod found for service {Service} in namespace {Namespace}",
                    serviceName, namespaceName);
                throw new InvalidOperationException($"No pod found for service {serviceName}");
            }

            // Start kubectl port-forward process
            var kubeConfig = _configuration.GetValue<string>("KUBECONFIG") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

            var startInfo = new ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = $"port-forward -n {namespaceName} pod/{podName} {localPort}:{targetPort}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            // Set KUBECONFIG environment variable
            startInfo.EnvironmentVariables["KUBECONFIG"] = kubeConfig;

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start kubectl port-forward process");
            }

            // Wait a bit to ensure port-forward is established
            await Task.Delay(2000, cancellationToken);

            // Check if process is still running
            if (process.HasExited)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError(
                    "Port-forward process exited immediately. Error: {Error}",
                    error);
                throw new InvalidOperationException($"Port-forward failed: {error}");
            }

            var url = $"http://localhost:{localPort}";
            var info = new PortForwardInfo
            {
                NamespaceName = namespaceName,
                ServiceName = serviceName,
                PodName = podName,
                LocalPort = localPort,
                TargetPort = targetPort,
                Url = url,
                Process = process,
                CreatedAt = DateTime.UtcNow
            };

            _portForwards[key] = info;

            _logger.LogInformation(
                "✅ Port-forward created: {Url} -> {Pod}:{TargetPort} in namespace {Namespace}",
                url, podName, targetPort, namespaceName);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create port-forward for {Service} in {Namespace}",
                serviceName, namespaceName);
            throw;
        }
    }

    /// <summary>
    /// Creates port-forwards for all services in a namespace.
    /// Returns a dictionary of service names to their local URLs.
    /// </summary>
    public async Task<Dictionary<string, string>> CreatePortForwardsForNamespaceAsync(
        string namespaceName,
        Dictionary<string, int> servicePorts,
        CancellationToken cancellationToken = default)
    {
        var urls = new Dictionary<string, string>();

        foreach (var (serviceName, port) in servicePorts)
        {
            try
            {
                var url = await CreatePortForwardAsync(namespaceName, serviceName, port, cancellationToken);
                urls[serviceName] = url;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to create port-forward for service {Service}, will continue with others",
                    serviceName);
            }
        }

        return urls;
    }

    /// <summary>
    /// Stops and removes all port-forwards for a specific namespace.
    /// </summary>
    public void CleanupNamespace(string namespaceName)
    {
        var keysToRemove = _portForwards.Keys
            .Where(k => k.StartsWith($"{namespaceName}/"))
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_portForwards.TryRemove(key, out var info))
            {
                StopPortForward(info);
            }
        }

        _logger.LogInformation(
            "Cleaned up {Count} port-forward(s) for namespace {Namespace}",
            keysToRemove.Count, namespaceName);
    }

    /// <summary>
    /// Gets all active port-forwards.
    /// </summary>
    public IReadOnlyDictionary<string, PortForwardInfo> GetActivePortForwards()
    {
        return _portForwards;
    }

    private async Task<string> GetPodNameForServiceAsync(
        string namespaceName,
        string serviceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var kubeConfig = _configuration.GetValue<string>("KUBECONFIG") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

            var startInfo = new ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = $"get pods -n {namespaceName} -l app={serviceName} -o jsonpath={{.items[0].metadata.name}}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            startInfo.EnvironmentVariables["KUBECONFIG"] = kubeConfig;

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return string.Empty;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return output.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pod name for service {Service}", serviceName);
            return string.Empty;
        }
    }

    private async Task<int> FindAvailablePortAsync(int preferredPort, CancellationToken cancellationToken)
    {
        // Skip privileged ports (<1024) on Windows - they require admin access
        // If preferredPort is privileged, use 8000+ offset instead
        if (preferredPort < 1024)
        {
            preferredPort = 8000 + preferredPort; // e.g., 80 -> 8080, 443 -> 8443
            _logger.LogInformation(
                "Original port was privileged, using {Port} instead",
                preferredPort);
        }
        
        // Check if port is available
        if (await IsPortAvailableAsync(preferredPort))
        {
            _logger.LogInformation("Using port {Port}", preferredPort);
            return preferredPort;
        }

        // Port is busy - kill whatever is using it (likely old port-forward from previous run)
        _logger.LogWarning("Port {Port} is busy, killing process using it...", preferredPort);
        await KillProcessUsingPortAsync(preferredPort);
        
        // Wait a moment for the port to be released
        await Task.Delay(500, cancellationToken);
        
        // Verify port is now available
        if (await IsPortAvailableAsync(preferredPort))
        {
            _logger.LogInformation("Successfully freed port {Port}", preferredPort);
            return preferredPort;
        }

        throw new InvalidOperationException($"Failed to free port {preferredPort} - it may be used by a system process");
    }

    private async Task KillProcessUsingPortAsync(int port)
    {
        try
        {
            // On Windows, use netstat to find the process ID using the port
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse netstat output to find PID
            // Format: TCP    0.0.0.0:3000    0.0.0.0:0    LISTENING    12345
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains($":{port}") && line.Contains("LISTENING"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                    {
                        _logger.LogInformation("Killing process {PID} using port {Port}", pid, port);
                        try
                        {
                            var processToKill = Process.GetProcessById(pid);
                            processToKill.Kill(entireProcessTree: true);
                            _logger.LogInformation("Successfully killed process {PID}", pid);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to kill process {PID}", pid);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find/kill process using port {Port}", port);
        }
    }

    /// <summary>
    /// Kills user run port-forwards (excluding infrastructure ports 6379/27017) before starting a new run.
    /// Since we only support 1 run at a time, this ensures ports are available.
    /// </summary>
    public async Task CleanupAllPortForwardsAsync()
    {
        try
        {
            // Kill kubectl port-forwards EXCEPT infrastructure (6379=Redis, 27017=MongoDB)
            var script = @"
                Get-Process kubectl -ErrorAction SilentlyContinue | ForEach-Object {
                    $cmdLine = (Get-WmiObject Win32_Process -Filter ""ProcessId=$($_.Id)"").CommandLine
                    if ($cmdLine -and $cmdLine -match 'port-forward' -and 
                        $cmdLine -notmatch ':6379' -and $cmdLine -notmatch ':27017') {
                        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                    }
                }
            ";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                _logger.LogInformation("Cleaned up user run port-forwards (preserved infrastructure 6379/27017)");
            }

            // Clear the in-memory cache
            _portForwards.Clear();

            // Wait a moment for ports to be released
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup port-forwards");
        }
    }

    private Task<bool> IsPortAvailableAsync(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private void StopPortForward(PortForwardInfo info)
    {
        try
        {
            if (info.Process != null && !info.Process.HasExited)
            {
                info.Process.Kill(entireProcessTree: true);
                info.Process.Dispose();
                
                _logger.LogInformation(
                    "Stopped port-forward for {Service} (localhost:{LocalPort})",
                    info.ServiceName, info.LocalPort);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to stop port-forward process for {Service}",
                info.ServiceName);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop all port-forwards
        foreach (var info in _portForwards.Values)
        {
            StopPortForward(info);
        }

        _portForwards.Clear();
        _logger.LogInformation("PortForwardManager disposed, all port-forwards stopped");
    }
}

/// <summary>
/// Information about an active port-forward.
/// </summary>
public class PortForwardInfo
{
    public required string NamespaceName { get; init; }
    public required string ServiceName { get; init; }
    public required string PodName { get; init; }
    public required int LocalPort { get; init; }
    public required int TargetPort { get; init; }
    public required string Url { get; init; }
    public required Process Process { get; init; }
    public required DateTime CreatedAt { get; init; }
}
