using k8s.Models;
using RepoRunner.Contracts.Events;

namespace Runner.Services;

public interface IKubernetesResourceGenerator
{
    /// <summary>
    /// Generate Kubernetes resources for DOCKERFILE mode (single service)
    /// </summary>
    Task<KubernetesResources> GenerateDockerfileModeResourcesAsync(
        string runId,
        string repoUrl,
        string imageRef,
        List<int> ports,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate Kubernetes resources for COMPOSE mode (multiple services)
    /// </summary>
    Task<KubernetesResources> GenerateComposeModeResourcesAsync(
        string runId,
        string repoUrl,
        string primaryService,
        List<ServiceInfo> services,
        CancellationToken cancellationToken = default);
}

public class KubernetesResources
{
    public string NamespaceName { get; set; } = string.Empty;
    public V1Namespace Namespace { get; set; } = null!;
    public List<V1Deployment> Deployments { get; set; } = new();
    public List<V1Service> Services { get; set; } = new();
    public int ExposedPort { get; set; }
    public Dictionary<string, List<int>> ServicePorts { get; set; } = new(); // Track all ports per service
}
