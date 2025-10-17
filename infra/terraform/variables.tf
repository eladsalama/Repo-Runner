variable "cluster_name" {
  description = "Name of the kind cluster"
  type        = string
  default     = "reporunner"
}

variable "namespace" {
  description = "Namespace for infrastructure services"
  type        = string
  default     = "infra"
}
