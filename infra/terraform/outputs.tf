# Cluster
output "kubeconfig_path" {
  description = "kubeconfig for kind"
  value       = "${path.module}/kubeconfig"
}

output "cluster_name" {
  description = "kind cluster name"
  value       = "reporunner"
}

# Internal service endpoints (in-cluster DNS)
output "kafka_endpoint" {
  value = "kafka.infra.svc.cluster.local:9092"
}

output "mongodb_endpoint" {
  value = "mongodb.infra.svc.cluster.local:27017"
}

output "redis_endpoint" {
  value = "redis-master.infra.svc.cluster.local:6379"
}

output "otel_collector_grpc_endpoint" {
  value = "otel-collector.infra.svc.cluster.local:4317"
}

output "otel_collector_http_endpoint" {
  value = "otel-collector.infra.svc.cluster.local:4318"
}

# Convenience
output "localhost_ports" {
  value = {
    preview_urls = "30080"
  }
}
