# RepoRunner

**Run any GitHub repo locally in isolated Kubernetes sandboxes—just click a button.**

One-click deployment of any public GitHub repo (Dockerfile or docker-compose.yml) into isolated K8s namespaces on your PC. Real-time build logs, automatic preview URLs, zero cloud costs.

## Features

- **One-Click Deploy**: Browser extension detects Dockerfiles, builds & runs automatically
- **Isolated Sandboxes**: Each run gets its own Kubernetes namespace with resource limits
- **Real-Time Logs**: Live build and runtime logs streamed to browser
- **Preview URLs**: Instant access to running apps via NodePort
- **Multi-Service Support**: Full docker-compose.yml support with service selection
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
.\bootstrap.ps1 apply  # Takes 5-10 min
.\bootstrap.ps1 verify
```

Creates: kind cluster + Redis + MongoDB + NodePort 30080

---

### 3. Start Services

```powershell
cd ..  # Back to repo root
.\start-services.ps1
```

**What this does:**
- Auto port-forwards MongoDB (27017) & Redis (6379)
- Starts Gateway, Orchestrator, Builder, Runner in separate windows
- Services auto-connect to local databases

⏱️ Wait ~10s for all services to show "Application started"

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

1. Open any GitHub repo with a Dockerfile (e.g., https://github.com/docker/docker-bench-security)
2. Click green **"Run Locally"** button (top-right)
3. Click **"Start Run"**
4. Watch: `QUEUED → BUILDING → RUNNING`
5. Access preview at http://localhost:30080 when status = `RUNNING`

**docker-compose repos:** Extension auto-detects and lets you select primary service

---

## Monitoring

**Service Logs:** Check the PowerShell windows opened by `start-services.ps1`

**Run Status:**
```powershell
kubectl get namespaces | Select-String "run-"  # List all runs
kubectl get pods -n run-<run-id>               # Check run pods
kubectl logs -n run-<run-id> <pod-name>        # View pod logs
```

**Database:**
```powershell
kubectl port-forward -n infra svc/mongodb 27018:27017
mongosh mongodb://localhost:27018
use reporunner
db.runs.find().pretty()
```

---

## Cleanup

**Stop services:** Close PowerShell windows + stop port-forward jobs:
```powershell
Get-Job | Where-Object { $_.Name -like "PortForward*" } | Stop-Job | Remove-Job
```

**Destroy infrastructure:**
```powershell
cd infra
.\bootstrap.ps1 destroy
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **Extension button missing** | Refresh page (Ctrl+Shift+R). Check F12 Console for errors. Verify repo has `Dockerfile` or `docker-compose.yml` in root. |
| **"Failed to start" error** | Ensure Gateway is running (`.\start-services.ps1`). Check http://localhost:5247 is accessible. |
| **Build fails: "git not found"** | `choco install git -y` then restart Builder service |
| **Build fails: "docker not found"** | Verify Docker Desktop is running: `docker ps` |
| **Pods stuck in "Pending"** | Increase Docker Desktop RAM to 6GB+ (Settings → Resources) |
| **Services crash immediately** | Check port-forward jobs are running: `Get-Job`. Restart with `.\start-services.ps1` |

---

## Documentation

- **[QUICKREF.md](QUICKREF.md)** - Command cheat sheet
- **[docs/Progress.md](docs/Progress.md)** - Milestone tracking & roadmap
- **[docs/Plan.md](docs/Plan.md)** - Architecture deep-dive
- **[infra/PREREQUISITES.md](infra/PREREQUISITES.md)** - Detailed tool installation

---

## What's Next

- **Milestone 8**: RAG Indexer (embed README + Dockerfiles)
- **Milestone 9**: LLM Chat (Q&A about running repos)
- **Milestone 10-11**: Security hardening + demo video

See [Progress.md](docs/Progress.md) for full roadmap.

---

## License

MIT • Portfolio project showcasing local-first K8s orchestration and event-driven architecture.
