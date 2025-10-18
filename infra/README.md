# RepoRunner Infrastructure

This directory contains Terraform configuration for provisioning the local development infrastructure for **RepoRunner**.

## Overview

The infrastructure runs entirely on your local machine using:
- **kind** (Kubernetes in Docker) - local K8s cluster
- **Kafka** - event streaming (Bitnami chart, single-node for local demo)
- **Redis Stack** - Redis with RediSearch for vector storage
- **MongoDB Community** - document database for metadata
- **OpenTelemetry Collector** - observability backend
- **Jaeger** - distributed tracing UI

All services run in a `infra` namespace within the kind cluster.

## Prerequisites

### Required Tools

1. **Docker Desktop** (or Rancher Desktop, Colima)
   - Windows: [Docker Desktop for Windows](https://docs.docker.com/desktop/install/windows-install/)
   - Ensure Docker daemon is running

2. **kubectl** - Kubernetes CLI
   ```powershell
   # Windows (via Chocolatey)
   choco install kubernetes-cli
   
   # Or download from https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/
   ```

3. **Helm** - Kubernetes package manager
   ```powershell
   # Windows (via Chocolatey)
   choco install kubernetes-helm
   
   # Or download from https://helm.sh/docs/intro/install/
   ```

4. **Terraform** - Infrastructure as Code
   ```powershell
   # Windows (via Chocolatey)
   choco install terraform
   
   # Or download from https://www.terraform.io/downloads
   ```

5. **kind CLI** - Kubernetes in Docker
   ```powershell
   # Windows (via Chocolatey)
   choco install kind
   
   # Or download from https://kind.sigs.k8s.io/docs/user/quick-start/#installation
   ```

### Verify Prerequisites

```powershell
# Check Docker is running
docker version

# Check tools are installed
kubectl version --client
helm version
terraform version
kind version
```

## Quick Start

### 1. Initialize Terraform

```powershell
cd infra/terraform
terraform init
```

This downloads the required providers (kind, helm, kubernetes).

### 2. Apply Infrastructure

```powershell
terraform apply
```

Review the plan and type `yes` to proceed. This will:
- Create a kind cluster named `reporunner` (1 control-plane + 1 worker node)
- Install all infrastructure services via Helm
- Configure port mappings for localhost access

**Expected time:** 5-10 minutes depending on your internet speed and machine.

### 3. Verify Deployment

```powershell
# Check cluster is running
kind get clusters

# Check all pods are healthy
kubectl get pods -n infra

# Expected output (all Running):
# NAME                              READY   STATUS    RESTARTS   AGE
# mongodb-xxxxxxxxxx-xxxxx          1/1     Running   0          5m
# otel-collector-xxxxxxxxxx-xxxxx   1/1     Running   0          5m
# kafka-0                           1/1     Running   0          5m
# redis-master-0                    1/1     Running   0          5m
# jaeger-xxxxxxxxxx-xxxxx           1/1     Running   0          5m
```

### 4. Access Services

Once deployed, you can access:

- **Jaeger UI**: http://localhost:30082
- **Preview URLs** (for running apps): http://localhost:30080/run-<runId>

- Internal services (accessible from within the cluster):
- **Kafka**: `kafka.infra.svc.cluster.local:9092`
- **Redis**: `redis-master.infra.svc.cluster.local:6379`
- **MongoDB**: `mongodb.infra.svc.cluster.local:27017`
- **OTel Collector**: `otel-collector.infra.svc.cluster.local:4317` (gRPC)

### 5. View Outputs

```powershell
terraform output
```

This shows all service endpoints and connection strings.

## Management Commands

### Check Status

```powershell
# List all pods
kubectl get pods -n infra

# Check a specific service
kubectl describe pod -n infra -l app.kubernetes.io/name=mongodb

# View logs
kubectl logs -n infra -l app.kubernetes.io/name=kafka --tail=50
```

### Port Forward (if needed)

```powershell
# Access Redis CLI
kubectl port-forward -n infra svc/redis-master 6379:6379

# Access MongoDB
kubectl port-forward -n infra svc/mongodb 27017:27017

# Access Kafka (no admin API by default for Bitnami chart)
# Use port-forwarding to reach brokers if needed. Example (for consumer tooling):
# kubectl port-forward -n infra svc/kafka 9092:9092
```

## Cleanup

### Destroy Infrastructure

```powershell
# Remove all services and cluster
terraform destroy

# Type 'yes' to confirm
```

This will:
1. Delete all Helm releases
2. Delete the kind cluster
3. Clean up all local resources

**Note:** Persistent data will be lost. If you want to preserve data, back up volumes first.

### Complete Reset

```powershell
# If terraform destroy fails or you want a hard reset:
kind delete cluster --name reporunner

# Then remove Terraform state
rm -r .terraform
rm terraform.tfstate*

# Re-initialize
terraform init
terraform apply
```

## Troubleshooting

### Pods not starting

```powershell
# Check pod events
kubectl describe pod <pod-name> -n infra

# Common issues:
# - ImagePullBackOff: Docker daemon not running or no internet
# - CrashLoopBackOff: Check logs with kubectl logs
# - Pending: Not enough resources (increase Docker Desktop memory/CPU)
```

### Port conflicts

If ports 30080-30082 are already in use:

1. Edit `main.tf` and change the `extra_port_mappings` host_port values
2. Run `terraform apply` to update

### Out of memory

Increase Docker Desktop resources:
- Docker Desktop → Settings → Resources
- Recommended: 8GB RAM, 4 CPUs minimum

### Kafka not starting

Kafka (Bitnami single-node) may fail if resources are constrained. If it fails:
- Check logs: `kubectl logs -n infra kafka-0 --all-containers`
- Ensure Docker has enough memory (increase in Docker Desktop resources)

### Clean slate

```powershell
# Nuclear option - removes everything
kind delete cluster --name reporunner
docker system prune -a --volumes
cd infra/terraform
rm -r .terraform
rm terraform.tfstate*
terraform init
terraform apply
```

## Configuration

### Customizing Resources

Edit `main.tf` to adjust resource limits:

```hcl
resources = {
  requests = {
    memory = "512Mi"
    cpu    = "250m"
  }
  limits = {
    memory = "1Gi"
    cpu    = "500m"
  }
}
```

### Adding Helm Repositories

```powershell
# List current repos
helm repo list

# Update repos
helm repo update

# Search for charts
helm search repo redis
```

## Next Steps

After infrastructure is running:
1. Proceed to **Milestone 2**: Protobuf Contracts & gRPC Scaffolds
2. Build .NET services that connect to these endpoints
3. Deploy services to the same kind cluster

## Architecture

```
┌───────────────────────────────────────────────────────┐
│ kind cluster: reporunner                              │
│                                                       │
│  ┌─────────────────────────────────────────────────┐  │
│  │ Namespace: infra                                │  │
│  │                                                 │  │
│  │  • Kafka (broker)       :9092                   │  │
│  │  • Redis Stack          :6379                   │  │
│  │  • MongoDB              :27017                  │  │
│  │  • OTel Collector       :4317, :4318            │  │
│  │  • Jaeger               :30082 (NodePort)       │  │
│  └─────────────────────────────────────────────────┘  │
│                                                       │
│  ┌─────────────────────────────────────────────────┐  │
│  │ Namespace: default (future services)            │  │
│  │  • Gateway                                      │  │
│  │  • Orchestrator                                 │  │
│  │  • Builder, Runner, Indexer, Insights           │  │
│  └─────────────────────────────────────────────────┘  │
│                                                       │
│  ┌─────────────────────────────────────────────────┐  │
│  │ Namespace: run-* (per-run isolation)            │  │
│  │  • User apps (ephemeral)                        │  │
│  └─────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────┘
         │                        │
      :30080                  :30082
         │                        │
    Preview URLs            Jaeger UI
         │                        │
         └────────────────────────┘
                localhost
```

## Zero-Cost Guarantee

All components used are free and open-source:
- ✅ kind (Apache 2.0)
- ✅ Kafka (Bitnami chart for local demo)
- ✅ Redis Stack (SSPL/RSALv2)
- ✅ MongoDB Community (SSPL)
- ✅ OpenTelemetry (Apache 2.0)
- ✅ Jaeger (Apache 2.0)

No cloud services or paid subscriptions required for MVP.
