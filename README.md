# RepoRunner — Local Kubernetes Orchestration Platform

> **A browser extension for one-click deployment of any GitHub repository into isolated Kubernetes sandboxes**

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.x-blue?logo=typescript)](https://www.typescriptlang.org/)
[![Kubernetes](https://img.shields.io/badge/Kubernetes-kind-326CE5?logo=kubernetes)](https://kind.sigs.k8s.io/)
[![Terraform](https://img.shields.io/badge/Terraform-IaC-7B42BC?logo=terraform)](https://www.terraform.io/)
[![MongoDB](https://img.shields.io/badge/MongoDB-6.0-47A248?logo=mongodb)](https://www.mongodb.com/)
[![Redis](https://img.shields.io/badge/Redis-Stack-DC382D?logo=redis)](https://redis.io/)
[![Docker](https://img.shields.io/badge/Docker-Containerized-2496ED?logo=docker)](https://www.docker.com/)

Built by [Elad Salama](https://www.linkedin.com/in/eladsalama)

---

## Project Overview

RepoRunner enables users to one-click deploy any public GitHub repo (with a Dockerfile or docker-compose.yml) to local Kubernetes.

<details open>
  <summary><b>Architecture Overview</b> (click to collapse)</summary>

  **Event-Driven Microservices Architecture:**
  
  - **Browser Extension (TypeScript)** → Detects repos, triggers deployments, streams logs
  - **Gateway (.NET 9)** → HTTP/gRPC API, request validation, authentication
  - **Orchestrator (.NET 9)** → Workflow coordination, state management, event orchestration
  - **Builder (.NET 9)** → Git cloning, Docker image builds, kind cluster loading
  - **Runner (.NET 9)** → Kubernetes deployments, namespace management, port-forwarding
  - **Redis Streams** → Event bus for async communication and task queues
  - **MongoDB** → Persistent storage for run metadata, logs, and artifacts
  - **Kubernetes (kind)** → Local container orchestration with namespace isolation
  - **Terraform** → Infrastructure as Code for reproducible local environment setup

  <p align="center">
    <i>Distributed system with gRPC communication, Redis-backed event streaming, and Kubernetes orchestration</i>
  </p>
</details>

---

## Technical Highlights

### **Zero-Cost Local Development**
- **No cloud dependencies** — Everything runs on Docker Desktop with kind (Kubernetes in Docker)
- **Infrastructure as Code** — HashiCorp Terraform provisions entire stack (kind cluster, Helm releases, networking) in 2-3 minutes
- **Self-healing infrastructure** — Automated port-forward monitoring with health checks and auto-restart on failure
- **Resource efficient** — 6GB RAM requirement, BuildKit-powered intelligent layer caching, multi-stage builds

### **Enterprise Patterns**
- **Event-driven architecture** — Redis Streams as message broker for reliable asynchronous communication and task queuing
- **Service mesh ready** — Protocol Buffers (protobuf) with gRPC for internal service-to-service communication, HTTP/REST gateway for external APIs
- **Observability-first design** — Structured logging (Serilog), RESTful health checks, distributed tracing hooks, service discovery
- **Fault tolerance** — Exponential backoff retry mechanisms, idempotency tokens, circuit breaker pattern, graceful degradation strategies

---

## Quick Start

### 1. Prerequisites

**Windows with Chocolatey:**
```powershell
choco install docker-desktop kubernetes-cli kubernetes-helm terraform kind git -y
```

**Docker Desktop:** Allocate **6GB+ RAM** (Settings → Resources)

Full install guide: [`infra/PREREQUISITES.md`](infra/PREREQUISITES.md)

---

### 2. Deploy Infrastructure

```powershell
git clone https://github.com/eladsalama/Repo-Runner.git
cd Repo-Runner\infra
.\bootstrap.ps1 apply  # Takes 2-3 min
.\set-kubeconfig.ps1
.\bootstrap.ps1 verify
```

Creates: kind cluster + Redis + MongoDB

---

### 3. Start Services (Background)

**One command to start all services:**
```powershell
cd ..  # Back to repo root
.\scripts\start-background.ps1
```

**What this does:**
- Checks if services are already running (idempotent execution)
- Starts any stopped services as **PowerShell background jobs** (IHostedService workers)
- Services run persistently in background without blocking terminal sessions
- Automated port-forwarding with health monitoring for MongoDB (27017) and Redis (6379)

First start takes approximately 10 seconds for .NET runtime initialization and service discovery. Subsequent executions detect already-running processes instantly via job status checks.

**Note:** Microservices run once and persist. Restart only required after code changes or configuration updates.

---

### 4. Load Extension

**Chrome/Edge:**
1. Go to `chrome://extensions/` (enable Developer mode)
2. Click **Load unpacked** → Select `<repo-root>/extension/dist`

**First time?** Build extension:
```powershell
cd extension
npm install
npm run build
```

---

### 5. Try It!

1. Open any GitHub repo with a Dockerfile (e.g., https://github.com/eladsalama/stock-dashboard)
2. Click green **"Run Locally"** button (top-right)
3. Click **"Start Run"**
4. Watch progress: `QUEUED → BUILDING → DEPLOYING → SUCCEEDED`
5. Click **"Open Preview"** button to access your deployed app (automatic port-forwarding!)

**docker-compose repos:** Extension auto-detects multi-service configurations and provides runtime toggle between Compose/Dockerfile deployment modes

**What happens automatically:**
- Docker images built with BuildKit and loaded directly into kind cluster node (no registry push required)
- Kubernetes namespace provisioned with resource quotas, network policies, and RBAC
- Environment variables and secrets parsed from docker-compose.yml and injected as ConfigMaps
- Database schema migrations executed automatically (Prisma, Entity Framework, Alembic detection)
- kubectl port-forwards established to localhost with intelligent port selection (preserves native ports: frontend→3000, backend→8080)
- Preview URL resolved to localhost with automatic service discovery

---

## Cleanup

**Stop a specific run:** Click "Stop Run" button in extension (cleans up namespace + port-forwards)

**Stop all services:**
```powershell
.\scripts\stop-services.ps1
```

**Destroy infrastructure:**
```powershell
cd infra
.\bootstrap.ps1 destroy
```

---

## Monitoring and Observability

**Service Logs:** Use the monitoring script to aggregate logs from all microservices:
```powershell
.\scripts\monitor-logs.ps1
```

**Run Status and Kubernetes Resources:**
```powershell
kubectl get namespaces | Select-String "run-"  # List all active run namespaces
kubectl get pods -n run-<run-id>               # Check pod status and readiness
kubectl logs -n run-<run-id> <pod-name>        # View container logs
kubectl describe pod -n run-<run-id> <pod-name> # Debug pod events and conditions
```

**Active Port-Forward Processes:**
```powershell
Get-Process | Where-Object { $_.ProcessName -eq 'kubectl' -and $_.CommandLine -like '*port-forward*' }
```

**MongoDB Database Inspection:**
```powershell
kubectl port-forward -n infra svc/mongodb 27018:27017
mongosh mongodb://localhost:27018
use reporunner
db.runs.find().pretty()
db.runs.aggregate([{ $group: { _id: "$status", count: { $sum: 1 } } }])  # Status breakdown
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **Extension button missing** | Refresh page (Ctrl+Shift+R). Inspect browser console (F12) for JavaScript errors. Verify repository root contains `Dockerfile` or `docker-compose.yml`. Check extension is loaded in `chrome://extensions/`. |
| **"Failed to start" error** | Ensure Gateway microservice is running (`.\scripts\start-background.ps1`). Verify health endpoint: `http://localhost:5247/health`. Check PowerShell background jobs: `Get-Job`. |
| **Build fails: "git not found"** | Install Git via Chocolatey: `choco install git -y`, then restart all services to refresh PATH environment variable. |
| **Build fails: "docker not found"** | Verify Docker Desktop is running: `docker ps`. Ensure Docker CLI is accessible: `docker --version`. Check Docker daemon status in system tray. |
| **Pods stuck in "Pending"** | Increase Docker Desktop resource allocation to 6GB+ RAM (Settings → Resources → Memory). Check node capacity: `kubectl describe nodes`. |
| **Status stuck at "Building"** | Infrastructure port-forwards (MongoDB/Redis) may have terminated. Execute `.\scripts\ensure-port-forwards.ps1` to restart. Port-forward monitor now auto-restarts failed connections. |
| **"Run not found" errors in logs** | MongoDB or Redis port-forward conflict with application services on ports 27017/6379. Fixed in v1.1+ with infrastructure port filtering logic. Update to latest version. |
| **Preview URL won't open** | Verify port-forward process is active: `Get-Process kubectl`. Application may be using reserved infrastructure ports (27017, 6379) — now automatically excluded from forwarding. |
| **Services fail to build after code changes** | Stop all services: `.\scripts\stop-services.ps1`. Rebuild affected service: `dotnet build -c Release`. Restart: `.\scripts\start-background.ps1`. |
| **Redis connection timeout** | Check Redis pod status: `kubectl get pods -n infra`. Verify port-forward: `Get-Job -Name "PortForwardRedis"`. Restart infrastructure: `.\infra\bootstrap.ps1 apply`. |

---

## Documentation

- **[AUDIT-REPORT.md](AUDIT-REPORT.md)** - Comprehensive system validation report with end-to-end test results
- **[PORT-MAPPING.md](PORT-MAPPING.md)** - Complete port allocation reference for all services and infrastructure
- **[QUICKREF.md](QUICKREF.md)** - Command reference and common operations cheat sheet
- **[infra/PREREQUISITES.md](infra/PREREQUISITES.md)** - Detailed tool installation instructions for Windows, macOS, Linux

**Quick Validation:**
```powershell
.\scripts\quick-check.ps1  # Pre-demo health check for all services and infrastructure
```

---

## Key Learning Outcomes

This project demonstrates:

### **Distributed Systems & Backend Engineering**
- Event-driven microservices with Redis Streams message broker and consumer groups
- gRPC with Protocol Buffers for type-safe inter-service communication
- .NET 9 backend with dependency injection, async/await patterns, and IHostedService workers
- MongoDB document storage with aggregation pipelines
- Idempotency tokens and exactly-once processing patterns
- Structured logging with Serilog and distributed tracing with correlation IDs

### **Infrastructure as Code & Kubernetes**
- HashiCorp Terraform for declarative infrastructure provisioning
- kind (Kubernetes in Docker) cluster with custom networking
- Helm chart deployments for stateful services (MongoDB ReplicaSet, Redis StatefulSet)
- Dynamic namespace provisioning with resource quotas and network policies
- ConfigMap/Secret management with automatic injection
- Automated port-forwarding and service discovery

### **DevOps & Containerization**
- Multi-stage Docker builds with BuildKit caching and layer optimization
- Docker Compose orchestration with profile filtering
- Local container registry with kind cluster image loading
- Port conflict detection and automated resolution

### **Browser Extension Development**
- TypeScript Chrome extension with Manifest V3
- Content scripts for GitHub repo detection and DOM manipulation
- Real-time status polling with exponential backoff
- YAML parsing for docker-compose.yml service discovery
