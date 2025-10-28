using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Builder.Services;

public class DockerComposeParser : IDockerComposeParser
{
    private readonly ILogger<DockerComposeParser> _logger;

    public DockerComposeParser(ILogger<DockerComposeParser> logger)
    {
        _logger = logger;
    }

    public async Task<List<ComposeService>> ParseAsync(string composePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing docker-compose file: {Path}", composePath);

        try
        {
            var yaml = await File.ReadAllTextAsync(composePath, cancellationToken);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var composeFile = deserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            if (!composeFile.TryGetValue("services", out var servicesObj) || servicesObj is not Dictionary<object, object> services)
            {
                throw new InvalidOperationException("No services found in docker-compose.yml");
            }

            var result = new List<ComposeService>();

            foreach (var kvp in services)
            {
                var serviceName = kvp.Key.ToString() ?? "unknown";
                var serviceConfig = kvp.Value as Dictionary<object, object>;
                
                if (serviceConfig == null)
                {
                    _logger.LogWarning("Service {Service} has invalid configuration", serviceName);
                    continue;
                }

                // Skip services with profiles (they need explicit activation with --profile)
                if (serviceConfig.TryGetValue("profiles", out var profilesObj))
                {
                    _logger.LogInformation("Skipping service {Service} - has profiles defined (not in default profile)", serviceName);
                    continue;
                }

                var composeService = new ComposeService { Name = serviceName };

                // Check for image
                if (serviceConfig.TryGetValue("image", out var imageObj))
                {
                    composeService.ImageRef = imageObj?.ToString();
                }

                // Check for build context
                if (serviceConfig.TryGetValue("build", out var buildObj))
                {
                    composeService.HasBuildContext = true;
                    
                    if (buildObj is string buildContext)
                    {
                        composeService.BuildContext = buildContext;
                    }
                    else if (buildObj is Dictionary<object, object> buildConfig)
                    {
                        if (buildConfig.TryGetValue("context", out var contextObj))
                        {
                            composeService.BuildContext = contextObj?.ToString() ?? ".";
                        }
                        if (buildConfig.TryGetValue("dockerfile", out var dockerfileObj))
                        {
                            composeService.Dockerfile = dockerfileObj?.ToString();
                        }
                    }
                }

                // Parse ports
                if (serviceConfig.TryGetValue("ports", out var portsObj) && portsObj is List<object> portsList)
                {
                    foreach (var portObj in portsList)
                    {
                        var portStr = portObj?.ToString();
                        if (string.IsNullOrEmpty(portStr)) continue;

                        // Handle formats like "8080:80", "80", or "${VAR:-3000}:${VAR:-3000}"
                        // Split carefully - don't split on : inside ${}
                        var parts = SplitPortMapping(portStr);
                        var portPart = parts.Length > 1 ? parts[1] : parts[0];
                        
                        // Remove /tcp or /udp suffix
                        portPart = portPart.Split('/')[0];

                        // Handle environment variable format: ${VAR:-default} or ${VAR}
                        if (portPart.StartsWith("${") && portPart.EndsWith("}"))
                        {
                            var envVarContent = portPart.Substring(2, portPart.Length - 3); // Remove ${ and }
                            
                            // Check if it has a default value (:-syntax)
                            if (envVarContent.Contains(":-"))
                            {
                                var defaultValue = envVarContent.Split(":-")[1];
                                if (int.TryParse(defaultValue, out var defaultPort))
                                {
                                    composeService.Ports.Add(defaultPort);
                                    _logger.LogInformation(
                                        "Resolved port environment variable in {Service}: {PortStr} -> {Port}",
                                        serviceName, portStr, defaultPort);
                                    continue;
                                }
                            }
                            
                            // Try to resolve from actual environment variables
                            var varName = envVarContent.Split(':')[0];
                            if (composeService.Environment.TryGetValue(varName, out var envValue))
                            {
                                // Recursively resolve if the value is also an env var
                                if (envValue.StartsWith("${") && envValue.Contains(":-"))
                                {
                                    var innerDefault = envValue.Split(":-")[1].TrimEnd('}');
                                    if (int.TryParse(innerDefault, out var resolvedPort))
                                    {
                                        composeService.Ports.Add(resolvedPort);
                                        continue;
                                    }
                                }
                                else if (int.TryParse(envValue, out var envPort))
                                {
                                    composeService.Ports.Add(envPort);
                                    continue;
                                }
                            }
                            
                            _logger.LogWarning(
                                "Could not resolve port environment variable in {Service}: {PortStr}, skipping",
                                serviceName, portStr);
                            continue;
                        }

                        if (int.TryParse(portPart, out var port))
                        {
                            composeService.Ports.Add(port);
                        }
                    }
                }

                // If no ports found, check expose
                if (composeService.Ports.Count == 0 && serviceConfig.TryGetValue("expose", out var exposeObj) && exposeObj is List<object> exposeList)
                {
                    foreach (var exposePortObj in exposeList)
                    {
                        if (int.TryParse(exposePortObj?.ToString(), out var port))
                        {
                            composeService.Ports.Add(port);
                        }
                    }
                }

                // Infer HTTP port if none specified and service name suggests web service
                if (composeService.Ports.Count == 0)
                {
                    var webServiceNames = new[] { "web", "app", "frontend", "api", "server", "nginx", "apache" };
                    if (webServiceNames.Any(name => serviceName.ToLowerInvariant().Contains(name)))
                    {
                        _logger.LogInformation(
                            "Service {Service} appears to be a web service but has no ports defined. Assuming port 80.",
                            serviceName);
                        composeService.Ports.Add(80);
                    }
                }

                // Parse environment variables
                if (serviceConfig.TryGetValue("environment", out var envObj))
                {
                    if (envObj is Dictionary<object, object> envDict)
                    {
                        // Format: key: value
                        foreach (var envKvp in envDict)
                        {
                            var key = envKvp.Key?.ToString();
                            var value = envKvp.Value?.ToString();
                            if (!string.IsNullOrEmpty(key) && value != null)
                            {
                                composeService.Environment[key] = value;
                            }
                        }
                    }
                    else if (envObj is List<object> envList)
                    {
                        // Format: - KEY=value
                        foreach (var envItem in envList)
                        {
                            var envStr = envItem?.ToString();
                            if (string.IsNullOrEmpty(envStr)) continue;
                            
                            var parts = envStr.Split('=', 2);
                            if (parts.Length == 2)
                            {
                                composeService.Environment[parts[0]] = parts[1];
                            }
                        }
                    }
                }

                result.Add(composeService);
                _logger.LogInformation(
                    "Parsed service: {Service}, Image: {Image}, BuildContext: {Context}, Ports: {Ports}",
                    serviceName, 
                    composeService.ImageRef ?? "N/A",
                    composeService.BuildContext ?? "N/A",
                    string.Join(", ", composeService.Ports));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse docker-compose file {Path}", composePath);
            throw;
        }
    }

    /// <summary>
    /// Splits port mapping string like "8080:80" or "${APP_PORT:-3000}:${APP_PORT:-3000}"
    /// Handles environment variables correctly without splitting on : inside ${}
    /// </summary>
    private static string[] SplitPortMapping(string portMapping)
    {
        var parts = new List<string>();
        var currentPart = new StringBuilder();
        var insideBraces = 0;

        for (int i = 0; i < portMapping.Length; i++)
        {
            var ch = portMapping[i];

            if (ch == '$' && i + 1 < portMapping.Length && portMapping[i + 1] == '{')
            {
                insideBraces++;
                currentPart.Append(ch);
            }
            else if (ch == '}' && insideBraces > 0)
            {
                insideBraces--;
                currentPart.Append(ch);
            }
            else if (ch == ':' && insideBraces == 0)
            {
                // This is a port separator, not part of an env var
                parts.Add(currentPart.ToString());
                currentPart.Clear();
            }
            else
            {
                currentPart.Append(ch);
            }
        }

        // Add the last part
        if (currentPart.Length > 0)
        {
            parts.Add(currentPart.ToString());
        }

        return parts.ToArray();
    }
}
