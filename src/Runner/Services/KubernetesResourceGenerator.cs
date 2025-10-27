using k8s.Models;
using RepoRunner.Contracts.Events;

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
            var servicePorts = service.Ports.ToList();
            if (!servicePorts.Any())
            {
                servicePorts.Add(80); // Default fallback
            }

            resources.Deployments.Add(CreateDeployment(
                namespaceName,
                service.Name,
                service.ImageRef,
                servicePorts,
                runId,
                service.Environment.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));

            resources.Services.Add(CreateService(
                namespaceName,
                service.Name,
                servicePorts));

            // Track service ports
            resources.ServicePorts[service.Name] = servicePorts;

            // Track which port to expose externally (primary service - use first port)
            if (service.Name == primaryService)
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

        // Convert environment dictionary to Kubernetes env vars
        var envVars = new List<V1EnvVar>();
        if (environment != null && environment.Any())
        {
            envVars = environment.Select(kvp => new V1EnvVar
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
                        Volumes = IsBuiltImage(imageRef) ? new List<V1Volume>
                        {
                            new V1Volume
                            {
                                Name = "tmp",
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
                                // Cover common writable paths: /tmp for temp files, /app/.next for Next.js cache
                                VolumeMounts = IsBuiltImage(imageRef) ? new List<V1VolumeMount>
                                {
                                    new V1VolumeMount
                                    {
                                        Name = "tmp",
                                        MountPath = "/tmp"
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
}

