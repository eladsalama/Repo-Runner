# RepoRunner - Quick Reference

## 📋 What Gets Started

```
Your Windows PC
  └── WSL2 (VmmemWSL ~4GB RAM)  # to configure, copy .wslconfig.example to C:\Users\salam\.wslconfig
      └── Docker Desktop
          └── kind Kubernetes cluster
              └── 4 services: Kafka, MongoDB, Redis, OTel Collector
```

**Important:** These run in the BACKGROUND after starting!

---

## 🚀 Essential Commands

### Start Infrastructure
```powershell
cd infra
.\bootstrap.ps1 apply  # Takes 5-10 min, terminal is BUSY
```
**What happens:** Terminal blocked, can't type. When you see "Done!" - services are running in background.

### Check What's Running
```powershell
.\bootstrap.ps1 status
```

### Stop Infrastructure (IMPORTANT!)
```powershell
.\bootstrap.ps1 destroy  # Run AFTER apply finishes
```
**Always use this to stop!** Closes terminal = services still running!

### If Apply Fails or Prerequisites Missing
**Automatic safe cleanup!** The script will:
- Detect missing tools (helm, terraform, kind, etc.)
- Stop immediately before starting anything
- Clean up any partially deployed resources
- Exit safely without orphaned processes

**If you interrupt with Ctrl+C:**
- Script automatically cleans up
- No manual destroy needed
- Safe to retry after fixing issues

### Verify Health
```powershell
.\bootstrap.ps1 verify
kubectl get pods -n infra
```

---

## 🧠 Fix High Memory (VmmemWSL)

**Problem:** VmmemWSL using 8GB RAM in Task Manager

**Solution:**

1. **Create file:** `C:\Users\<YourUsername>\.wslconfig`
```ini
[wsl2]
memory=4GB
processors=2
swap=2GB
```

2. **Restart WSL2** (PowerShell as Admin):
```powershell
wsl --shutdown
```

3. **Wait 10 seconds** - Docker restarts automatically

**Adjust memory based on total RAM:**
- 8GB total → `memory=3GB`
- 16GB total → `memory=4GB` 
- 32GB+ → `memory=6GB` or `memory=8GB`

---

## 🔧 Daily Operations
```powershell
# Check status
.\bootstrap.ps1 status

# View all pods
kubectl get pods -n infra

# View pod logs
kubectl logs -n infra <pod-name>

# Restart a service
kubectl rollout restart deployment/<name> -n infra
kubectl rollout restart statefulset/<name> -n infra
```

### Cleanup
```powershell
# Destroy infrastructure (run AFTER apply finishes)
# Infrastructure must be deployed first
.\bootstrap.ps1 destroy

# Complete reset (if things are broken)
.\bootstrap.ps1 reset

# If apply is still running and you want to stop:
# 1. Press Ctrl+C to interrupt
# 2. Then run: .\bootstrap.ps1 destroy
```

---

## 🌐 Access Services
- **Jaeger**: http://localhost:30082
- **Preview**: http://localhost:30080

---

## 🔌 Port Forwarding
```powershell
# MongoDB
kubectl port-forward -n infra svc/mongodb 27017:27017

# Redis
kubectl port-forward -n infra svc/redis-master 6379:6379

# Kafka (broker)
# Port-forward the kafka service to reach it from localhost (for testing/tools)
kubectl port-forward -n infra svc/kafka 9092:9092
```

---

## 🔍 Troubleshooting
```powershell
# Check pod events
kubectl describe pod -n infra <pod-name>

# Check all events
kubectl get events -n infra --sort-by='.lastTimestamp'

# Check if ports are in use
netstat -ano | findstr "30080"
netstat -ano | findstr "30082"

# Check Docker
docker ps
docker stats

# View Terraform outputs
cd infra/terraform
terraform output
```

---

## 📡 Service Connection Strings

**From within cluster (for .NET services):**
```
MongoDB:  mongodb://mongodb.infra.svc.cluster.local:27017
Redis:    redis-master.infra.svc.cluster.local:6379
Kafka: kafka.infra.svc.cluster.local:9092
OTel:     otel-collector.infra.svc.cluster.local:4317
```

**From localhost (for testing):**
```powershell
# Use port-forward commands above
```

---

## ✅ Quick Health Check
```powershell
# One-liner to check all pods
kubectl get pods -n infra | Select-String "Running"

# Count running pods (should be 5)
(kubectl get pods -n infra -o json | ConvertFrom-Json).items | Where-Object { $_.status.phase -eq "Running" } | Measure-Object | Select-Object -ExpandProperty Count
```

---

## 📝 Logs
```powershell
# All logs from infra namespace
kubectl logs -n infra --all-containers=true --tail=100

# Follow logs from specific service
kubectl logs -n infra -l app.kubernetes.io/name=mongodb -f

# Export logs to file
kubectl logs -n infra <pod-name> > logs.txt
```

---

## 🆘 Emergency Reset
```powershell
# If everything is broken
kind delete cluster --name reporunner
cd infra
Remove-Item -Recurse -Force terraform/.terraform
Remove-Item -Force terraform/terraform.tfstate*
.\bootstrap.ps1 apply
```

---

## 📁 File Locations

```
infra/
├── terraform/main.tf         # Infrastructure definition
├── terraform/outputs.tf      # Service endpoints
├── bootstrap.ps1             # Main automation script
├── validate.ps1              # Health check script
├── README.md                 # Full documentation
└── PREREQUISITES.md          # Installation guide

docs/
└── Progress.md               # Milestone tracking (THE SOURCE OF TRUTH)
```

---

## ❓ Need More Help?

- **Full docs**: `infra/README.md`
- **Prerequisites**: `infra/PREREQUISITES.md`
- **Milestones**: `docs/Progress.md`
