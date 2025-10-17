# Cluster configuration
output "kubeconfig_path" {
  description = "Path to kubeconfig file for the kind cluster"
  value       = kind_cluster.reporunner.kubeconfig_path
}

output "cluster_name" {
  description = "Name of the kind cluster"
  value       = kind_cluster.reporunner.name
}

# Service endpoints
output "kafka_endpoint" {
  description = "Kafka endpoint (internal cluster)"
  value       = "kafka.infra.svc.cluster.local:9092"
}

output "redis_endpoint" {
  description = "Redis endpoint (internal cluster)"
  value       = "redis-master.infra.svc.cluster.local:6379"
}

output "mongodb_endpoint" {
  description = "MongoDB endpoint (internal cluster)"
  value       = "mongodb.infra.svc.cluster.local:27017"
}

output "otel_collector_grpc_endpoint" {
  description = "OpenTelemetry Collector gRPC endpoint (internal cluster)"
  value       = "otel-collector.infra.svc.cluster.local:4317"
}

output "otel_collector_http_endpoint" {
  description = "OpenTelemetry Collector HTTP endpoint (internal cluster)"
  value       = "otel-collector.infra.svc.cluster.local:4318"
}

# Connection strings
output "mongodb_connection_string" {
  description = "MongoDB connection string for applications"
  value       = "mongodb://mongodb.infra.svc.cluster.local:27017"
  sensitive   = false
}

output "redis_connection_string" {
  description = "Redis connection string for applications"
  value       = "redis-master.infra.svc.cluster.local:6379"
  sensitive   = false
}

# Port mappings for localhost access
output "localhost_ports" {
  description = "Port mappings for accessing services from localhost"
  value = {
    preview_urls = "30080"
  }
}
