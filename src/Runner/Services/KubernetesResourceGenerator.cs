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
        var port = ports.FirstOrDefault(8080);

        _logger.LogInformation(
            "Generating DOCKERFILE mode resources for RunId={RunId}, Namespace={Namespace}, Port={Port}",
            runId, namespaceName, port);

        var resources = new KubernetesResources
        {
            NamespaceName = namespaceName,
            Namespace = CreateNamespace(namespaceName, runId, repoUrl, RunMode.Dockerfile),
            ExposedPort = port
        };

        // Create deployment
        resources.Deployments.Add(CreateDeployment(
            namespaceName,
            serviceName,
            imageRef,
            port,
            runId));

        // Create service
        resources.Services.Add(CreateService(
            namespaceName,
            serviceName,
            port));

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
            var port = service.Ports.FirstOrDefault(80);

            resources.Deployments.Add(CreateDeployment(
                namespaceName,
                service.Name,
                service.ImageRef,
                port,
                runId));

            resources.Services.Add(CreateService(
                namespaceName,
                service.Name,
                port));

            // Track which port to expose externally (primary service)
            if (service.Name == primaryService)
            {
                resources.ExposedPort = port;
            }
        }

        // If no primary service port set, use first service port
        if (resources.ExposedPort == 0 && services.Any())
        {
            resources.ExposedPort = services.First().Ports.FirstOrDefault(80);
        }

        return resources;
    }

    private V1Namespace CreateNamespace(string name, string runId, string repoUrl, RunMode mode)
    {
        var ttlHours = _configuration.GetValue<int>("Runner:NamespaceTTLHours", 2);
        var deleteAfter = DateTime.UtcNow.AddHours(ttlHours);

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
                    ["reporunner.io/created-at"] = DateTime.UtcNow.ToString("o")
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
        int port,
        string runId)
    {
        var cpuLimit = _configuration.GetValue<string>("Runner:CpuLimit", "500m");
        var memLimit = _configuration.GetValue<string>("Runner:MemoryLimit", "512Mi");

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
                        Containers = new List<V1Container>
                        {
                            new V1Container
                            {
                                Name = serviceName,
                                Image = imageRef,
                                ImagePullPolicy = "Never", // Images loaded into kind
                                Ports = new List<V1ContainerPort>
                                {
                                    new V1ContainerPort
                                    {
                                        ContainerPort = port,
                                        Protocol = "TCP"
                                    }
                                },
                                Resources = new V1ResourceRequirements
                                {
                                    Limits = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity(cpuLimit),
                                        ["memory"] = new ResourceQuantity(memLimit)
                                    },
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity("100m"),
                                        ["memory"] = new ResourceQuantity("128Mi")
                                    }
                                },
                                // Security context - non-root, read-only rootfs
                                SecurityContext = new V1SecurityContext
                                {
                                    RunAsNonRoot = true,
                                    RunAsUser = 1000,
                                    ReadOnlyRootFilesystem = false, // Many apps need writable /tmp
                                    AllowPrivilegeEscalation = false,
                                    Capabilities = new V1Capabilities
                                    {
                                        Drop = new List<string> { "ALL" }
                                    }
                                }
                            }
                        },
                        // Pod-level security context
                        SecurityContext = new V1PodSecurityContext
                        {
                            RunAsNonRoot = true,
                            RunAsUser = 1000,
                            FsGroup = 1000
                        }
                    }
                }
            }
        };
    }

    private V1Service CreateService(string namespaceName, string serviceName, int port)
    {
        return new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = serviceName
                }
            },
            Spec = new V1ServiceSpec
            {
                Type = "ClusterIP",
                Selector = new Dictionary<string, string>
                {
                    ["app"] = serviceName
                },
                Ports = new List<V1ServicePort>
                {
                    new V1ServicePort
                    {
                        Port = port,
                        TargetPort = port,
                        Protocol = "TCP"
                    }
                }
            }
        };
    }
}
