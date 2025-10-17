# Terraform configuration - STABLE VERSIONS
terraform {
  required_version = ">= 1.0"
  required_providers {
    kind = {
      source  = "tehcyx/kind"
      version = "~> 0.4"
    }
    helm = {
      source  = "hashicorp/helm"
      version = "~> 2.12"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 2.25"
    }
  }
}

resource "kind_cluster" "reporunner" {
  name            = "reporunner"
  wait_for_ready  = true
  kubeconfig_path = pathexpand("~/.kube/config")

  kind_config {
    kind        = "Cluster"
    api_version = "kind.x-k8s.io/v1alpha4"

    node {
      role = "control-plane"
      extra_port_mappings {
        container_port = 30080
        host_port      = 30080
        protocol       = "TCP"
      }
    }
  }
}

provider "kubernetes" {
  config_path = kind_cluster.reporunner.kubeconfig_path
}

provider "helm" {
  kubernetes {
    config_path = kind_cluster.reporunner.kubeconfig_path
  }
}

resource "kubernetes_namespace" "infra" {
  metadata {
    name = "infra"
    labels = {
      "app.kubernetes.io/managed-by" = "terraform"
    }
  }
  depends_on = [kind_cluster.reporunner]
}

# Kafka - Let Helm use latest available
resource "helm_release" "kafka" {
  name       = "kafka"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "kafka"
  namespace  = kubernetes_namespace.infra.metadata[0].name

  values = [yamlencode({
    controller = { replicaCount = 1 }
    zookeeper = { enabled = false }
    kraft = { enabled = true }
    persistence = { enabled = false }
    resources = {
      requests = { memory = "1Gi", cpu = "500m" }
      limits = { memory = "2Gi", cpu = "2000m" }
    }
    heapOpts = "-Xmx1g -Xms1g"
    auth = {
      clientProtocol = "plaintext"
      interBrokerProtocol = "plaintext"
    }
    image = {
      registry = "registry.bitnami.com"
    }
  })]

  timeout = 900  # 15 minutes - plenty of time for first image pull
  wait = false
  depends_on = [kubernetes_namespace.infra]
}

# MongoDB - Let Helm use latest available
resource "helm_release" "mongodb" {
  name       = "mongodb"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "mongodb"
  namespace  = kubernetes_namespace.infra.metadata[0].name

  values = [yamlencode({
    architecture = "standalone"
    auth = { enabled = false }
    persistence = { enabled = false }
    resources = {
      requests = { memory = "512Mi", cpu = "250m" }
      limits = { memory = "1Gi", cpu = "1000m" }
    }
    image = {
      registry = "registry.bitnami.com"
    }
  })]

  timeout = 900  # 15 minutes
  wait = false
  depends_on = [kubernetes_namespace.infra]
}

# Redis - Let Helm use latest available
resource "helm_release" "redis" {
  name       = "redis"
  repository = "https://charts.bitnami.com/bitnami"
  chart      = "redis"
  namespace  = kubernetes_namespace.infra.metadata[0].name

  values = [yamlencode({
    architecture = "standalone"
    auth = { enabled = false }
    master = {
      persistence = { enabled = false }
      resources = {
        requests = { memory = "256Mi", cpu = "250m" }
        limits = { memory = "512Mi", cpu = "500m" }
      }
    }
    replica = { replicaCount = 0 }
    image = {
      registry = "registry.bitnami.com"
    }
  })]

  timeout = 900  # 15 minutes
  wait = false
  depends_on = [kubernetes_namespace.infra]
}

# OpenTelemetry - Let Helm use latest available
resource "helm_release" "otel_collector" {
  name       = "otel-collector"
  repository = "https://open-telemetry.github.io/opentelemetry-helm-charts"
  chart      = "opentelemetry-collector"
  namespace  = kubernetes_namespace.infra.metadata[0].name

  values = [yamlencode({
    mode = "deployment"
    replicaCount = 1
     image = {
       repository = "otel/opentelemetry-collector-contrib"
       tag        = "0.99.0"
     }
    resources = {
      requests = { memory = "128Mi", cpu = "100m" }
      limits = { memory = "256Mi", cpu = "200m" }
    }
    config = {
      receivers = {
        otlp = {
          protocols = {
            grpc = { endpoint = "0.0.0.0:4317" }
            http = { endpoint = "0.0.0.0:4318" }
          }
        }
      }
      processors = {
        batch = {}
        memory_limiter = {
          check_interval = "1s"
          limit_mib = 100
        }
      }
      exporters = {
        logging = { loglevel = "info" }
      }
      service = {
        pipelines = {
          traces = {
            receivers = ["otlp"]
            processors = ["memory_limiter", "batch"]
            exporters = ["logging"]
          }
          metrics = {
            receivers = ["otlp"]
            processors = ["memory_limiter", "batch"]
            exporters = ["logging"]
          }
          logs = {
            receivers = ["otlp"]
            processors = ["memory_limiter", "batch"]
            exporters = ["logging"]
          }
        }
      }
    }
  })]

  timeout = 900  # 15 minutes
  wait = false
  depends_on = [kubernetes_namespace.infra]
}
