# RepoRunner

RepoRunner is a local-first tool that lets you clone any **public GitHub repo** with a Dockerfile, **build it**, and **run it** inside an **isolated Kubernetes sandbox** on your own machine. A built-in chat (“Ask the Repo”) answers questions about the codebase (tech stack, data flow, caching) using local RAG — no paid services required.

## Why
Showcase the ability to run arbitrary projects safely, not just my own, and to reason about unfamiliar repos quickly.

## Tech Stack (MVP)
- **Browser Extension**: TypeScript (Manifest V3), gRPC-Web to the gateway
- **Backend**: .NET 8 services (Gateway + Orchestrator + Workers) with **gRPC**
- **Events**: **Kafka** (Bitnami chart for local demo)
- **Data**: **MongoDB Community** (metadata, chat), **Redis Stack** (cache + vector search)
- **Runtime**: **Kubernetes** via **kind/k3d** (per-run namespaces, quotas, TTL cleanup)
- **Observability**: **OpenTelemetry** (traces/metrics/logs)
- **LLM/RAG**: **Ollama** locally (e.g., Llama 3.x 8B) + small open-source embeddings

## Status
<<<<<<< HEAD
🚧 **Work in progress** (MVP target): one-click "Run Locally" for a single-container HTTP app, basic logs, and "Ask the Repo" over README + Dockerfile with citations. Everything runs **free** on a laptop.

### Current Progress
- ✅ **Milestone 1**: Local infrastructure setup (Terraform + kind + Helm)
- ⏳ **Milestone 2**: Protobuf contracts & gRPC scaffolds
- 📋 See [`docs/Progress.md`](docs/Progress.md) for full roadmap

## Quick Start

### Prerequisites

You need Docker, kubectl, helm, terraform, and kind installed. See [`infra/PREREQUISITES.md`](infra/PREREQUISITES.md) for installation instructions.

**TL;DR for Windows with Chocolatey:**
```powershell
# Install Chocolatey first: https://chocolatey.org/install
choco install docker-desktop kubernetes-cli kubernetes-helm terraform kind -y
```

### 1. Deploy Infrastructure

```powershell
# Clone the repo
git clone https://github.com/eladsalama/Repo-Runner.git
cd Repo-Runner

# Deploy the local stack (kind cluster + all services)
cd infra
.\bootstrap.ps1 apply
```

### Deploy Infrastructure

```powershell
git clone https://github.com/eladsalama/Repo-Runner.git
cd Repo-Runner/infra
.\bootstrap.ps1 apply  # Takes 5-10 min
```

**⚠️ Runs in background!** Use `.\bootstrap.ps1 destroy` to stop.

### Verify & Access

```powershell
.\bootstrap.ps1 verify  # Check health
```

**Services:**
- **Jaeger**: http://localhost:30082
- **Preview**: http://localhost:30080

## Documentation

- **[QUICKREF.md](QUICKREF.md)**: 🎯 Essential commands (START HERE)
- **[Progress.md](docs/Progress.md)**: Milestone tracking
- **[Plan.md](docs/Plan.md)**: Full vision & architecture
- **[infra/README.md](infra/README.md)**: Infrastructure details
- **[infra/PREREQUISITES.md](infra/PREREQUISITES.md)**: Tool installation
=======
Work in progress (MVP target): one-click “Run Locally” for a single-container HTTP app, basic logs, and “Ask the Repo” over README + Dockerfile with citations.
>>>>>>> b4155d1ad63c4a0eb6b856bffc94af70fa41e853

