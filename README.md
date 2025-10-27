# RepoRunner

**Run any GitHub repo locally in isolated Kubernetes sandboxes—just click a button.**

One-click deployment of any public GitHub repo (Dockerfile or docker-compose.yml) into isolated K8s namespaces on your PC. Real-time build logs, automatic preview URLs, zero cloud costs.

## Features

- **One-Click Deploy**: Browser extension detects Dockerfiles, builds & runs automatically
- **Isolated Sandboxes**: Each run gets its own Kubernetes namespace with resource limits
- **Real-Time Logs**: Live build and runtime logs streamed to browser
- **Auto Port-Forwarding**: Deployed apps automatically accessible on localhost
- **Multi-Service Support**: Full docker-compose.yml support with automatic service detection
- **Auto-Migrations**: Prisma databases automatically initialized on deployment
- **Auto-Cleanup**: TTL-based namespace deletion (2h default)

## Stack

**.NET 9.0 Microservices** (Gateway, Orchestrator, Builder, Runner) • **Redis Streams** (events) • **MongoDB** (logs/metadata) • **kind** (local K8s) • **Browser Extension** (TypeScript)

---

## Quick Start

### 1. Prerequisites

**Windows with Chocolatey:**
```powershell
choco install docker-desktop kubernetes-cli kubernetes-helm terraform kind git -y
```

**Docker Desktop:** Allocate **6GB+ RAM** (Settings → Resources)

📖 Full install guide: [`infra/PREREQUISITES.md`](infra/PREREQUISITES.md)

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
- Checks if services are already running
- Starts any stopped services as **background jobs**
- Services keep running in background (no terminal windows)
- Auto port-forwards MongoDB (27017) & Redis (6379)

⏱️ First start takes ~10s for initialization. Subsequent runs detect already-running services instantly.

💡 **Tip:** Services run once and stay up. No need to restart unless you update code.

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

**docker-compose repos:** Extension auto-detects services and you can toggle between Compose/Dockerfile modes

**What happens automatically:**
- Docker images built and loaded into kind cluster
- Kubernetes namespace created with all services
- Environment variables parsed from docker-compose.yml
- Prisma migrations executed (if detected)
- Port-forwards created to localhost (prefers native ports: web→3100, api→3000)
- Preview URL points to your localhost

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


## Monitoring

**Service Logs:** Use the monitoring script to watch all services:
```powershell
.\scripts\monitor-logs.ps1
```

**Run Status:**
```powershell
kubectl get namespaces | Select-String "run-"  # List all runs
kubectl get pods -n run-<run-id>               # Check run pods
kubectl logs -n run-<run-id> <pod-name>        # View pod logs
```

**Active Port-Forwards:**
```powershell
Get-Process | Where-Object { $_.ProcessName -eq 'kubectl' -and $_.CommandLine -like '*port-forward*' }
```

**Database:**
```powershell
kubectl port-forward -n infra svc/mongodb 27018:27017
mongosh mongodb://localhost:27018
use reporunner
db.runs.find().pretty()
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **Extension button missing** | Refresh page (Ctrl+Shift+R). Check F12 Console for errors. Verify repo has `Dockerfile` or `docker-compose.yml` in root. |
| **"Failed to start" error** | Ensure Gateway is running (`.\scripts\start-background.ps1`). Check http://localhost:5247 is accessible. |
| **Build fails: "git not found"** | `choco install git -y` then restart services |
| **Build fails: "docker not found"** | Verify Docker Desktop is running: `docker ps` |
| **Pods stuck in "Pending"** | Increase Docker Desktop RAM to 6GB+ (Settings → Resources → Memory) |
| **"Port already in use"** | Another app is using the port. Runner will try nearby ports automatically (3100→3101→3102...) |
| **Preview URL won't open** | Check port-forward is active: `Get-Process kubectl`. Restart run if needed. |

---

## Documentation

- **[AUDIT-REPORT.md](AUDIT-REPORT.md)** - Comprehensive validation report (all systems verified ✅)
- **[PORT-MAPPING.md](PORT-MAPPING.md)** - Complete port allocation reference
- **[QUICKREF.md](QUICKREF.md)** - Command cheat sheet
- **[docs/Progress.md](docs/Progress.md)** - Milestone tracking & roadmap
- **[docs/Plan.md](docs/Plan.md)** - Architecture deep-dive
- **[infra/PREREQUISITES.md](infra/PREREQUISITES.md)** - Detailed tool installation

**Quick Validation:**
```powershell
.\scripts\quick-check.ps1  # Run before every demo
```

---

## What's Next

- **Milestone 8**: RAG Indexer (embed README + Dockerfiles)
- **Milestone 9**: LLM Chat (Q&A about running repos)
- **Milestone 10-11**: Security hardening + demo video

See [Progress.md](docs/Progress.md) for full roadmap.

---

## License

MIT • Portfolio project showcasing local-first K8s orchestration and event-driven architecture.
