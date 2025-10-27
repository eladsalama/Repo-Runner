############################################
# RepoRunner — minimal, clean main.tf (from zero)
# - Only declares provider sources (no version pins)
# - Single-node kind cluster (K8s v1.30.0)
# - Providers read a static kubeconfig path
# - One namespace: infra
# - Helm installs: Redis, MongoDB, Kafka (Bitnami chart), OTel Collector
############################################

terraform {
  required_providers {
    kind       = { source = "tehcyx/kind" }
    kubernetes = { source = "hashicorp/kubernetes" }
    helm       = { source = "hashicorp/helm" }
  }
}

# ---------- KIND CLUSTER ----------
resource "kind_cluster" "cluster" {
  name            = "reporunner"
  wait_for_ready  = true
  kubeconfig_path = "${path.module}/kubeconfig"  # deterministic, static path

  kind_config {
    api_version = "kind.x-k8s.io/v1alpha4"
    kind        = "Cluster"

    node {
      role  = "control-plane"
      image = "kindest/node:v1.30.0"

      # Port mappings for frontend (30080) and backend API (30081)
      extra_port_mappings {
        container_port = 30080
        host_port      = 30080
        protocol       = "TCP"
      }
      
      extra_port_mappings {
        container_port = 30081
        host_port      = 30081
        protocol       = "TCP"
      }
    }
  }
}

# Wait helper: ensure the Kubernetes API is responsive before creating k8s resources
resource "null_resource" "wait_for_kube" {
  depends_on = [kind_cluster.cluster]

  provisioner "local-exec" {
    interpreter = ["PowerShell", "-Command"]
    command = <<EOT
$env:KUBECONFIG = "${path.module}/kubeconfig"
for ($i = 0; $i -lt 60; $i++) {
  kubectl get nodes 2>$null
  if ($LASTEXITCODE -eq 0) { exit 0 }
  Start-Sleep -Seconds 2
}
Write-Error "Timed out waiting for kube-apiserver to become ready"
exit 1
EOT
  }
}

# ---------- PROVIDERS ----------
# IMPORTANT: point to the literal path (do NOT reference the resource here).
provider "kubernetes" {
  host                   = kind_cluster.cluster.endpoint
  client_certificate     = kind_cluster.cluster.client_certificate
  client_key             = kind_cluster.cluster.client_key
  cluster_ca_certificate = kind_cluster.cluster.cluster_ca_certificate
}

provider "helm" {
  kubernetes = {
    host                   = kind_cluster.cluster.endpoint
  client_certificate     = kind_cluster.cluster.client_certificate
  client_key             = kind_cluster.cluster.client_key
  cluster_ca_certificate = kind_cluster.cluster.cluster_ca_certificate
  }
}

# ---------- NAMESPACE ----------
resource "kubernetes_namespace" "infra" {
  metadata { name = "infra" }
  depends_on = [null_resource.wait_for_kube]
}

# ---------- REDIS (Bitnami) ----------
resource "helm_release" "redis" {
  name       = "redis"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "redis"
  namespace  = kubernetes_namespace.infra.metadata[0].name
  wait       = false
  timeout    = 300

  set = [
    { name = "architecture", value = "standalone" },
    { name = "auth.enabled", value = "false" },
    { name = "master.persistence.enabled", value = "false" },
    { name = "replica.replicaCount", value = "0" },
  ]

  depends_on = [kubernetes_namespace.infra]
}

# ---------- MONGODB (Bitnami) ----------
resource "helm_release" "mongodb" {
  name       = "mongodb"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "mongodb"
  namespace  = kubernetes_namespace.infra.metadata[0].name
  wait       = false
  timeout    = 300

  # Keep MongoDB minimal for local demo: standalone, no auth, no persistence.
  set = [
    { name = "architecture", value = "standalone" },
    { name = "auth.enabled", value = "false" },
    { name = "persistence.enabled", value = "false" },
  ]

  depends_on = [kubernetes_namespace.infra]
}

/* Kafka helm_release removed to keep this local demo minimal and avoid
   failing image pulls and chart compatibility issues. Re-add with a
   curated values file when ready to test Kafka. */

/* OpenTelemetry Collector removed to keep the local demo minimal and avoid
   configuration complexity. If you want Otel later, re-add the helm_release
   with a simple values file or the chart's defaults. */
