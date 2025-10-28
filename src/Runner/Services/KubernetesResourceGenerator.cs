using k8s.Models;
using RepoRunner.Contracts.Events;
using System.Linq;

namespace Runner.Services;

public class KubernetesResourceGenerator : IKubernetesResourceGenerator
{
    private readonly ILogger<KubernetesResourceGenerator> _logger;
    private readonly IConfiguration _configuration;

    public KubernetesResourceGenerator(
        ILogger<KubernetesResourceGenerator> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<KubernetesResources> GenerateDockerfileModeResourcesAsync(
        string runId,
        string repoUrl,
        string imageRef,
        List<int> ports,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Keep signature async for consistency

        var namespaceName = $"run-{runId}";
        var serviceName = "app";
        var primaryPort = ports.FirstOrDefault(8080);

        _logger.LogInformation(
            "Generating DOCKERFILE mode resources for RunId={RunId}, Namespace={Namespace}, Ports={Ports}",
            runId, namespaceName, string.Join(", ", ports));

        var resources = new KubernetesResources
        {
            NamespaceName = namespaceName,
            Namespace = CreateNamespace(namespaceName, runId, repoUrl, RunMode.Dockerfile),
            ExposedPort = primaryPort
        };

        // Create deployment with ALL ports
        resources.Deployments.Add(CreateDeployment(
            namespaceName,
            serviceName,
            imageRef,
            ports.Any() ? ports : new List<int> { 8080 },
            runId));

        // Create service with ALL ports
        resources.Services.Add(CreateService(
            namespaceName,
            serviceName,
            ports.Any() ? ports : new List<int> { 8080 }));

        // Track service ports
        resources.ServicePorts[serviceName] = ports.Any() ? ports : new List<int> { 8080 };

        return resources;
    }

    public async Task<KubernetesResources> GenerateComposeModeResourcesAsync(
        string runId,
        string repoUrl,
        string primaryService,
        List<ServiceInfo> services,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Keep signature async for consistency

        var namespaceName = $"run-{runId}";

        _logger.LogInformation(
            "Generating COMPOSE mode resources for RunId={RunId}, Namespace={Namespace}, PrimaryService={Primary}, Services={Count}",
            runId, namespaceName, primaryService, services.Count);

        var resources = new KubernetesResources
        {
            NamespaceName = namespaceName,
            Namespace = CreateNamespace(namespaceName, runId, repoUrl, RunMode.Compose)
        };

        // Generate deployment and service for each compose service
        foreach (var service in services)
        {
            // Sanitize service name for Kubernetes (replace underscores with hyphens)
            var k8sServiceName = SanitizeKubernetesName(service.Name);
            
            var servicePorts = service.Ports.ToList();
            if (!servicePorts.Any())
            {
                // Smart port detection: use well-known ports for common images
                var defaultPort = DetectDefaultPort(service.ImageRef, service.Name);
                servicePorts.Add(defaultPort);
            }

            resources.Deployments.Add(CreateDeployment(
                namespaceName,
                k8sServiceName,
                service.ImageRef,
                servicePorts,
                runId,
                service.Environment.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));

            resources.Services.Add(CreateService(
                namespaceName,
                k8sServiceName,
                servicePorts));

            // Track service ports using sanitized name
            resources.ServicePorts[k8sServiceName] = servicePorts;

            // Track which port to expose externally (primary service - use first port)
            // Compare using sanitized names
            if (SanitizeKubernetesName(primaryService) == k8sServiceName)
            {
                resources.ExposedPort = servicePorts.First();
            }
        }

        // If no primary service port set, use first service port
        if (resources.ExposedPort == 0 && services.Any())
        {
            var firstService = services.First();
            resources.ExposedPort = firstService.Ports.FirstOrDefault(80);
        }

        return resources;
    }

    private V1Namespace CreateNamespace(string name, string runId, string repoUrl, RunMode mode)
    {
        var ttlHours = _configuration.GetValue<int>("Runner:NamespaceTTLHours", 2);
        var deleteAfter = DateTime.UtcNow.AddHours(ttlHours);
        
        // Format timestamps for Kubernetes labels (no colons or special chars)
        var createdAtLabel = DateTime.UtcNow.ToString("yyyyMMddTHHmmss"); // e.g., "20251026T202715"

        return new V1Namespace
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/managed-by"] = "reporunner",
                    ["reporunner.io/run-id"] = runId,
                    ["reporunner.io/mode"] = mode.ToString().ToLowerInvariant(),
                    ["reporunner.io/created-at"] = createdAtLabel
                },
                Annotations = new Dictionary<string, string>
                {
                    ["reporunner.io/repo-url"] = repoUrl,
                    ["reporunner.io/delete-after"] = deleteAfter.ToString("o")
                }
            }
        };
    }

    private V1Deployment CreateDeployment(
        string namespaceName,
        string serviceName,
        string imageRef,
        List<int> ports,
        string runId,
        Dictionary<string, string>? environment = null)
    {
        var cpuLimit = _configuration.GetValue<string>("Runner:CpuLimit", "200m"); // Reduced from 500m
        var memLimit = _configuration.GetValue<string>("Runner:MemoryLimit", "256Mi"); // Reduced from 512Mi

        // Create container ports for all exposed ports
        var containerPorts = ports.Select(p => new V1ContainerPort
        {
            ContainerPort = p,
            Protocol = "TCP"
        }).ToList();

        // Convert environment dictionary to Kubernetes env vars with shell variable resolution
        var envVars = new List<V1EnvVar>();
        if (environment != null && environment.Any())
        {
            // Resolve shell variables in environment values
            var resolvedEnv = ResolveEnvironmentVariables(environment);
            
            envVars = resolvedEnv.Select(kvp => new V1EnvVar
            {
                Name = kvp.Key,
                Value = kvp.Value
            }).ToList();
        }

        return new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = serviceName,
                    ["reporunner.io/run-id"] = runId
                }
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>
                    {
                        ["app"] = serviceName
                    }
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = serviceName,
                            ["reporunner.io/run-id"] = runId
                        }
                    },
                    Spec = new V1PodSpec
                    {
                        // Skip pod-level security context for now - user images may not be built for non-root
                        // TODO: Add option to enforce security context for vetted images
                        SecurityContext = null,
                        // Provide emptyDir volumes for built images to have writable temp directories
                        // Include common paths that apps might expect (tmp, config, data, cache, etc.)
                        Volumes = IsBuiltImage(imageRef) ? new List<V1Volume>
                        {
                            new V1Volume
                            {
                                Name = "tmp",
                                EmptyDir = new V1EmptyDirVolumeSource()
                            },
                            new V1Volume
                            {
                                Name = "config",
                                EmptyDir = new V1EmptyDirVolumeSource()
                            },
                            new V1Volume
                            {
                                Name = "data",
                                EmptyDir = new V1EmptyDirVolumeSource()
                            }
                        } : null,
                        Containers = new List<V1Container>
                        {
                            new V1Container
                            {
                                Name = serviceName,
                                Image = imageRef,
                                // Use "Never" for built images (loaded into kind), "IfNotPresent" for external images
                                ImagePullPolicy = IsBuiltImage(imageRef) ? "Never" : "IfNotPresent",
                                Ports = containerPorts,
                                Env = envVars.Any() ? envVars : null, // Add environment variables if provided
                                // Mount writable volumes for built images that may need to write at runtime
                                // Cover common paths: /tmp, /app/config, /app/data for poorly configured apps
                                VolumeMounts = IsBuiltImage(imageRef) ? new List<V1VolumeMount>
                                {
                                    new V1VolumeMount
                                    {
                                        Name = "tmp",
                                        MountPath = "/tmp"
                                    },
                                    new V1VolumeMount
                                    {
                                        Name = "config",
                                        MountPath = "/app/config"
                                    },
                                    new V1VolumeMount
                                    {
                                        Name = "data",
                                        MountPath = "/app/data"
                                    }
                                } : null,
                                Resources = new V1ResourceRequirements
                                {
                                    Limits = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity(cpuLimit),
                                        ["memory"] = new ResourceQuantity(memLimit)
                                    },
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity("50m"), // Reduced from 100m
                                        ["memory"] = new ResourceQuantity("64Mi") // Reduced from 128Mi
                                    }
                                },
                                // Skip container-level security context for now - causes permission issues with user images
                                // User-provided Dockerfiles often don't set up proper permissions for non-root users
                                SecurityContext = null
                            }
                        }
                    }
                }
            }
        };
    }

    private V1Service CreateService(string namespaceName, string serviceName, List<int> ports)
    {
        // Create service ports for all exposed ports
        var servicePorts = ports.Select(p => new V1ServicePort
        {
            Port = p,
            TargetPort = p,
            Protocol = "TCP",
            Name = $"port-{p}",
            // Use NodePort to expose externally - K8s will auto-assign NodePort in 30000-32767 range
            NodePort = null // Auto-assign
        }).ToList();

        return new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = serviceName
                },
                Annotations = new Dictionary<string, string>
                {
                    ["reporunner.io/exposed-ports"] = string.Join(",", ports)
                }
            },
            Spec = new V1ServiceSpec
            {
                Type = "NodePort", // Changed from ClusterIP to NodePort for external access
                Selector = new Dictionary<string, string>
                {
                    ["app"] = serviceName
                },
                Ports = servicePorts
            }
        };
    }

    /// <summary>
    /// Determines if an image was built by us (and loaded into kind) vs external image
    /// Built images have format: {runId}-{serviceName}:latest (e.g., "abc123-api:latest")
    /// External images have format: {registry}/{image}:{tag} (e.g., "postgres:16", "redis:7")
    /// </summary>
    private bool IsBuiltImage(string imageRef)
    {
        // Built images contain a GUID-like runId prefix (contains hyphens, no slashes, no dots)
        // External images typically have registry names (contain / or .) or are short names (postgres, redis)
        if (imageRef.Contains('/') || imageRef.Contains('.'))
        {
            return false; // Has registry prefix or domain, must be external
        }

        // Check if it starts with GUID pattern (8+ hex chars followed by hyphen)
        var parts = imageRef.Split('-');
        if (parts.Length >= 2 && parts[0].Length >= 8)
        {
            return true; // Looks like runId-serviceName format
        }

        return false; // Short name like "postgres" or "redis", must be external
    }

    /// <summary>
    /// Detects the default port for a service based on its image or service name.
    /// Uses well-known ports for common databases and services.
    /// </summary>
    /// <param name="imageRef">Docker image reference</param>
    /// <param name="serviceName">Service name from docker-compose</param>
    /// <returns>Default port number</returns>
    private int DetectDefaultPort(string imageRef, string serviceName)
    {
        var imageLower = imageRef.ToLowerInvariant();
        var nameLower = serviceName.ToLowerInvariant();
        
        // Check image name for well-known services
        if (imageLower.Contains("mongo")) return 27017;
        if (imageLower.Contains("postgres") || imageLower.Contains("postgresql")) return 5432;
        if (imageLower.Contains("mysql") || imageLower.Contains("mariadb")) return 3306;
        if (imageLower.Contains("redis")) return 6379;
        if (imageLower.Contains("elasticsearch")) return 9200;
        if (imageLower.Contains("kibana")) return 5601;
        if (imageLower.Contains("rabbitmq")) return 5672;
        if (imageLower.Contains("kafka")) return 9092;
        if (imageLower.Contains("cassandra")) return 9042;
        if (imageLower.Contains("influxdb")) return 8086;
        if (imageLower.Contains("grafana")) return 3000;
        if (imageLower.Contains("prometheus")) return 9090;
        if (imageLower.Contains("nginx")) return 80;
        if (imageLower.Contains("apache") || imageLower.Contains("httpd")) return 80;
        
        // Check service name as fallback
        if (nameLower.Contains("mongo")) return 27017;
        if (nameLower.Contains("postgres") || nameLower.Contains("postgresql") || nameLower.Contains("pg")) return 5432;
        if (nameLower.Contains("mysql") || nameLower.Contains("mariadb")) return 3306;
        if (nameLower.Contains("redis")) return 6379;
        if (nameLower.Contains("elastic")) return 9200;
        if (nameLower.Contains("rabbit")) return 5672;
        if (nameLower.Contains("kafka")) return 9092;
        
        // Default to 80 for web services
        return 80;
    }

    /// <summary>
    /// Sanitizes a service name to be Kubernetes-compatible.
    /// Kubernetes resource names must follow RFC 1123: lowercase alphanumeric characters or '-',
    /// and must start and end with an alphanumeric character.
    /// </summary>
    /// <param name="name">Original service name (may contain underscores)</param>
    /// <returns>Kubernetes-compatible name (underscores replaced with hyphens)</returns>
    private string SanitizeKubernetesName(string name)
    {
        // Replace underscores with hyphens
        var sanitized = name.Replace('_', '-');
        
        // Convert to lowercase (Kubernetes requires lowercase)
        sanitized = sanitized.ToLowerInvariant();
        
        // Remove any other invalid characters (keep only alphanumeric and hyphens)
        sanitized = new string(sanitized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        
        // Ensure it starts and ends with alphanumeric (trim leading/trailing hyphens)
        sanitized = sanitized.Trim('-');
        
        return sanitized;
    }

    /// <summary>
    /// Resolves shell variable syntax in environment variable values.
    /// Handles patterns like ${VAR:-default}, ${VAR}, $VAR
    /// This allows poorly configured docker-compose files to work in Kubernetes.
    /// </summary>
    /// <param name="environment">Original environment variables</param>
    /// <returns>Resolved environment variables with shell syntax stripped</returns>
    private Dictionary<string, string> ResolveEnvironmentVariables(Dictionary<string, string> environment)
    {
        var resolved = new Dictionary<string, string>();
        
        foreach (var kvp in environment)
        {
            var value = kvp.Value;
            
            // Pattern 1: ${VAR:-default} - use default value
            // Example: ${APP_PORT:-3000} -> 3000
            var defaultPattern = @"\$\{([A-Za-z_][A-Za-z0-9_]*):?-([^}]+)\}";
            value = System.Text.RegularExpressions.Regex.Replace(value, defaultPattern, m => m.Groups[2].Value);
            
            // Pattern 2: ${VAR} or $VAR - try to resolve from current environment, otherwise remove
            // Example: ${MONGO_PORT} -> look up MONGO_PORT in resolved dict, or remove if not found
            var varPattern = @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}";
            value = System.Text.RegularExpressions.Regex.Replace(value, varPattern, m =>
            {
                var varName = m.Groups[1].Value;
                // Try to find in already resolved variables
                if (resolved.TryGetValue(varName, out var resolvedValue))
                {
                    return resolvedValue;
                }
                // Try to find in original environment
                if (environment.TryGetValue(varName, out var envValue))
                {
                    return envValue;
                }
                // Can't resolve, return empty string
                return string.Empty;
            });
            
            // Pattern 3: Simple $VAR syntax
            var simpleVarPattern = @"\$([A-Za-z_][A-Za-z0-9_]*)";
            value = System.Text.RegularExpressions.Regex.Replace(value, simpleVarPattern, m =>
            {
                var varName = m.Groups[1].Value;
                if (resolved.TryGetValue(varName, out var resolvedValue))
                {
                    return resolvedValue;
                }
                if (environment.TryGetValue(varName, out var envValue))
                {
                    return envValue;
                }
                return string.Empty;
            });
            
            resolved[kvp.Key] = value;
        }
        
        return resolved;
    }
}

