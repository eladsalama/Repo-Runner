# RepoRunner

RepoRunner is a local-first tool that lets you clone any **public GitHub repo** with a Dockerfile or docker-compose.yml, **build it**, and **run it** inside an **isolated Kubernetes sandbox** on your own machine. Built-in log streaming shows real-time build and runtime output. Everything runs **free** on your laptop—no cloud required.

## Why

Showcase the ability to run arbitrary projects safely (not just my own) in isolated K8s namespaces with resource quotas, TTL cleanup, and real-time observability. Demonstrates event-driven architecture, gRPC streaming, and RAG-based code intelligence.

## Tech Stack

**Frontend:**
- Browser Extension (TypeScript, Manifest V3, Webpack)
- REST + gRPC-Web to Gateway (streaming deferred to future)

**Backend (.NET 9.0):**
- **Gateway** - gRPC-Web termination, REST API, log streaming
- **Orchestrator** - Event coordination, run lifecycle management
- **Builder** - Git clone, Docker BuildKit, docker-compose parsing
- **Runner** - Kubernetes deployment, pod log tailing, preview URLs
- **Indexer** - RAG ingestion (future)
- **Insights** - LLM chat (future)

**Infrastructure:**
- **Events**: Redis Streams (Kafka/Redpanda deferred to stretch)
- **Data**: MongoDB (metadata, logs), Redis Stack (cache, vectors)
- **Runtime**: kind (Kubernetes in Docker), per-run namespaces
- **Observability**: OpenTelemetry, Jaeger
- **LLM/RAG** (future): Ollama (Llama 3.x 8B) + local embeddings

## Status

✅ **Milestones 1-7 Complete**:
- Infrastructure (kind + Redis + MongoDB + Kafka + OTel)
- Protobuf contracts & gRPC services
- Redis Streams event backbone
- Extension with auto-detection (Dockerfile vs docker-compose.yml)
- Builder (git clone + Docker build for both modes)
- Runner (K8s deploy for DOCKERFILE and COMPOSE modes)
- Log streaming (MongoDB storage, gRPC endpoint, extension UI)

🚧 **Next**: Milestone 8 (Indexer), Milestone 9 (Insights RAG chat)

---

## Quick Start Guide

### Prerequisites

Install these tools (Windows PowerShell with Chocolatey):

```powershell
# Install Chocolatey: https://chocolatey.org/install
choco install docker-desktop kubernetes-cli kubernetes-helm terraform kind git -y
```

**Manual install:** See [`infra/PREREQUISITES.md`](infra/PREREQUISITES.md)

**Memory:** Ensure Docker Desktop has at least **4GB RAM** allocated (Settings → Resources)

---

### 1. Deploy Infrastructure

```powershell
# Clone repo
git clone https://github.com/eladsalama/Repo-Runner.git
cd Repo-Runner

# Deploy local stack (kind cluster + Redis + MongoDB + Kafka + Jaeger)
cd infra
.\bootstrap.ps1 apply  # Takes 5-10 min on first run
```

**What this does:**
- Creates kind cluster (`reporunner`)
- Installs Redis Stack, MongoDB, Kafka, OTel Collector via Helm
- Exposes NodePort 30080 (preview URLs) and 30082 (Jaeger UI)

**Verify:**
```powershell
.\bootstrap.ps1 verify
kubectl get pods -n infra  # All should be Running
```

---

### 2. Build .NET Services

```powershell
cd ..  # Back to repo root
dotnet build  # Compiles all 6 services
```

**Expected output:** `Build succeeded in ~1-2s` with no warnings.

---

### 3. Configure Services

Each service needs MongoDB and Redis connection strings.

**Option A: Use `appsettings.json` defaults** (if MongoDB/Redis are port-forwarded to localhost):

```powershell
# Terminal 1: Port-forward MongoDB
kubectl port-forward -n infra svc/mongodb 27017:27017

# Terminal 2: Port-forward Redis
kubectl port-forward -n infra svc/redis-master 6379:6379
```

**Option B: Use cluster-internal endpoints** (recommended for production):

Edit `appsettings.json` in each service:
- **MongoDB**: `mongodb://mongodb.infra.svc.cluster.local:27017`
- **Redis**: `redis-master.infra.svc.cluster.local:6379`

---

### 4. Start Services

Open **5 separate PowerShell terminals** and run each service:

```powershell
# Terminal 1: Gateway (port 5247)
cd src/Gateway
dotnet run

# Terminal 2: Orchestrator
cd src/Orchestrator
dotnet run

# Terminal 3: Builder
cd src/Builder
dotnet run

# Terminal 4: Runner
cd src/Runner
dotnet run

# Terminal 5: Indexer (optional, not yet used)
cd src/Indexer
dotnet run
```

**Wait for:** Each service should log `"Application started"` or `"Now listening on"`.

---

### 5. Load Browser Extension

**Chrome/Edge:**
1. Navigate to `chrome://extensions/` (or `edge://extensions/`)
2. Enable **Developer mode** (top-right toggle)
3. Click **Load unpacked**
4. Select `<repo-root>/extension/dist` folder
5. Extension icon should appear in toolbar

**Build extension first** (if needed):
```powershell
cd extension
npm install
npm run build  # Creates dist/ folder
```

---

### 6. Test End-to-End Run

#### Test 1: Dockerfile Repo

1. **Open GitHub** in browser: https://github.com/docker/docker-bench-security
   - This repo has a Dockerfile in root
2. **Look for green "Run Locally" button** (top-right, near Watch/Fork/Star)
   - If missing, check console (F12) for errors
3. **Click "Run Locally"** → Dropdown opens
4. **Click "Start Run"** button
5. **Watch status transitions:**
   - `Starting...` → `QUEUED` → `BUILDING` → `RUNNING` → `SUCCEEDED`
6. **Check logs tab** in dropdown:
   - Click "Build" tab to see git clone + docker build logs
   - Click "Run" tab to see pod logs (once running)
7. **Access preview:** When status = `RUNNING`, preview URL appears (http://localhost:30080)

#### Test 2: docker-compose Repo

1. **Open GitHub**: https://github.com/docker/awesome-compose (or any compose repo)
2. **Click "Run Locally"** → Dropdown shows:
   - **Mode:** COMPOSE
   - **Primary Service:** Dropdown to select main service
3. **Click "Start Run"**
4. **Watch multi-service deployment:**
   - Builder builds all services with `build:` context
   - Runner deploys all services in same namespace
   - Only primaryService exposed via NodePort
5. **Check logs:**
   - "All" tab shows mixed logs
   - Click service-specific tab (e.g., "web") to filter

---

### 7. Monitor and Debug

#### Check Service Logs

```powershell
# Gateway logs (shows gRPC calls)
cd src/Gateway
dotnet run  # Terminal output

# Builder logs (shows git clone + docker build)
cd src/Builder
dotnet run

# Runner logs (shows K8s deployment + pod log tailing)
cd src/Runner
dotnet run

# Orchestrator logs (shows event consumption + status updates)
cd src/Orchestrator
dotnet run
```

#### Check MongoDB Logs

```powershell
# Connect to MongoDB
kubectl port-forward -n infra svc/mongodb 27017:27017
mongosh mongodb://localhost:27017

use reporunner
db.runs.find().pretty()  # See all runs
db.logs.find({ runId: "<run-id>" }).sort({ timestamp: 1 })  # See logs for specific run
```

#### Check Redis Cache

```powershell
# Connect to Redis
kubectl port-forward -n infra svc/redis-master 6379:6379
redis-cli

# Check cached run status
GET run:<run-id>:status
TTL run:<run-id>:status  # Should show ~7200 seconds (2 hours)
```

#### Check Kubernetes Deployments

```powershell
# List all run namespaces
kubectl get namespaces | Select-String "run-"

# Check pods in a run namespace
kubectl get pods -n run-<run-id>

# Check pod logs
kubectl logs -n run-<run-id> <pod-name>

# Check preview service
kubectl get svc -n run-<run-id>
```

#### View Traces in Jaeger

1. **Open Jaeger UI**: http://localhost:30082
2. **Select service:** `gateway`, `builder`, `runner`, `orchestrator`
3. **Click "Find Traces"**
4. **Inspect spans:** See full request flow from extension → Gateway → Orchestrator → Builder → Runner

---

### 8. Stop Run

1. **Click "Stop Run"** button in extension dropdown
2. **Orchestrator** receives `RunStopRequested` event
3. **Runner** deletes namespace (all pods/services removed)
4. **Status** updates to `STOPPED`

**Or stop manually:**
```powershell
kubectl delete namespace run-<run-id>
```

---

### 9. Cleanup

```powershell
# Stop all .NET services (Ctrl+C in each terminal)

# Destroy infrastructure (IMPORTANT!)
cd infra
.\bootstrap.ps1 destroy  # Removes kind cluster + all services

# Verify cleanup
kind get clusters  # Should show empty
docker ps  # Kind containers removed
```

**Memory note:** If VmmemWSL is using high RAM, see [`QUICKREF.md`](QUICKREF.md) for `.wslconfig` settings.

---

## Troubleshooting

### Extension button not appearing
- **Check**: Does repo have `Dockerfile` or `docker-compose.yml` in root?
- **Try**: Hard refresh page (Ctrl+Shift+R)
- **Console**: Open F12 → Console, look for `[RepoRunner]` logs

### "Failed to start" error in extension
- **Check**: Is Gateway running? (`dotnet run --project src/Gateway`)
- **URL**: Extension expects Gateway at `http://localhost:5247`
- **CORS**: Gateway has `AllowAll` CORS policy enabled

### Build fails with "git not found"
- **Install Git**: `choco install git -y`
- **Restart** Builder service after installing

### Build fails with "docker: command not found"
- **Check Docker**: `docker ps` should work from PowerShell
- **PATH**: Ensure Docker Desktop is installed and running

### Pods stuck in "Pending"
- **Resources**: Increase Docker Desktop memory (Settings → Resources → 6GB+)
- **Check nodes**: `kubectl describe nodes` → Look for resource pressure

### "Cannot connect to MongoDB/Redis"
- **Port-forward**: Ensure port-forward commands are running in separate terminals
- **Or**: Use cluster-internal endpoints in `appsettings.json`

### Services crash with "Connection refused"
- **Order**: Start services in order (Gateway → Orchestrator → Builder → Runner)
- **Wait**: Each service needs ~5s to start gRPC/HTTP listeners

---

## Project Structure

```
Repo-Runner/
├── contracts/               # Protobuf definitions
│   ├── run.proto           # RunService (StartRun, StopRun, StreamLogs)
│   ├── insights.proto      # InsightsService (AskRepo - future)
│   └── events.proto        # Redis Streams events
├── src/                    # .NET services
│   ├── Gateway/           # gRPC-Web + REST API (port 5247)
│   ├── Orchestrator/      # Event coordinator
│   ├── Builder/           # Git clone + Docker build
│   ├── Runner/            # K8s deployment + log tailing
│   ├── Indexer/           # RAG ingestion (future)
│   └── Insights/          # LLM chat (future)
├── shared/Shared/         # Shared libraries
│   ├── Models/            # LogEntry, Run
│   ├── Repositories/      # LogRepository, RunRepository
│   ├── Cache/             # RunStatusCache
│   └── Streams/           # Redis Streams abstractions
├── extension/             # Browser extension
│   ├── src/content.ts     # GitHub UI injection + log streaming
│   ├── src/popup.ts       # Extension popup (future chat UI)
│   └── manifest.json      # Manifest V3 config
├── infra/                 # Infrastructure
│   ├── terraform/         # kind + Helm charts
│   ├── bootstrap.ps1      # One-command setup
│   └── validate.ps1       # Health checks
└── docs/                  # Documentation
    ├── Progress.md        # Milestone tracking
    ├── Plan.md            # Architecture & vision
    └── progress summary/  # Per-milestone summaries
```

---

## Documentation

- **[QUICKREF.md](QUICKREF.md)**: Essential commands cheat sheet
- **[Progress.md](docs/Progress.md)**: Milestone tracking (source of truth)
- **[Plan.md](docs/Plan.md)**: Full vision, architecture, workflows
- **[infra/README.md](infra/README.md)**: Infrastructure details
- **[infra/PREREQUISITES.md](infra/PREREQUISITES.md)**: Tool installation
- **[Milestone Summaries](docs/progress%20summary/)**: Detailed per-milestone docs

---

## What's Next?

- **Milestone 8**: Indexer v1 (README + Dockerfile/docker-compose → Redis vectors + Mongo metadata)
- **Milestone 9**: Insights v1 (RAG + Ollama + Citations)
- **Milestone 10**: Hardening (security policies, quotas, network policies)
- **Milestone 11**: Documentation + demo video

See [`docs/Progress.md`](docs/Progress.md) for full roadmap.

---

## License

MIT

---

## Contributing

This is a portfolio/demo project. Issues and PRs welcome, but note it's primarily for showcasing local-first orchestration and RAG patterns.
