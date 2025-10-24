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
    /// Get preview URL for accessing the deployed service
    /// </summary>
    string GetPreviewUrl(string namespaceName, string serviceName, int port);
}
