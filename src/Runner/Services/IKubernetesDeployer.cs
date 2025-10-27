using k8s;

namespace Runner.Services;

public interface IKubernetesDeployer
{
    /// <summary>
    /// Deploy resources to Kubernetes cluster
    /// </summary>
    Task<string> DeployAsync(KubernetesResources resources, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete namespace and all resources within it
    /// </summary>
    Task DeleteNamespaceAsync(string namespaceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// List namespaces with a specific prefix
    /// </summary>
    Task<List<string>> ListNamespacesAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get preview URL for accessing the deployed service
    /// </summary>
    string GetPreviewUrl(string namespaceName, string serviceName, int port);

    /// <summary>
    /// Get all accessible service URLs (for multi-service/multi-port deployments)
    /// </summary>
    Dictionary<string, string> GetAllServiceUrls(KubernetesResources resources);
}
