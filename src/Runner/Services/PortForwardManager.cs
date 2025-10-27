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
        
        // Check if port-forward already exists
        if (_portForwards.TryGetValue(key, out var existing))
        {
            _logger.LogInformation(
                "Port-forward already exists for {Service} in {Namespace} on port {Port}",
                serviceName, namespaceName, existing.LocalPort);
            return existing.Url;
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
        // Always try preferred port first - this ensures web:3100 gets 3100, api:3000 gets 3000
        if (await IsPortAvailableAsync(preferredPort))
        {
            _logger.LogInformation("Using preferred port {Port}", preferredPort);
            return preferredPort;
        }

        _logger.LogWarning("Preferred port {Port} is busy, finding alternative...", preferredPort);

        // Try nearby ports first (±10 from preferred)
        for (int offset = 1; offset <= 10; offset++)
        {
            int lowerPort = preferredPort - offset;
            if (lowerPort >= 3000 && await IsPortAvailableAsync(lowerPort))
            {
                _logger.LogInformation("Using alternative port {Port} (preferred {PreferredPort} was busy)", lowerPort, preferredPort);
                return lowerPort;
            }

            int upperPort = preferredPort + offset;
            if (upperPort < 10000 && await IsPortAvailableAsync(upperPort))
            {
                _logger.LogInformation("Using alternative port {Port} (preferred {PreferredPort} was busy)", upperPort, preferredPort);
                return upperPort;
            }
        }

        // Fallback: scan entire range
        for (int port = 3000; port < 10000; port++)
        {
            if (port == preferredPort) continue; // Already tried
            if (await IsPortAvailableAsync(port))
            {
                _logger.LogInformation("Using fallback port {Port} (preferred {PreferredPort} was busy)", port, preferredPort);
                return port;
            }
        }

        throw new InvalidOperationException($"No available ports found (preferred: {preferredPort})");
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
