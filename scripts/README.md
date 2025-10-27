# RepoRunner Scripts

PowerShell scripts for starting, monitoring, and debugging RepoRunner services.

---

## 🎯 Recommended Workflow

### **Production-Grade Startup (Recommended)**
```powershell
# One-command demo launch
.\scripts\demo-launch.ps1

# With continuous monitoring
.\scripts\demo-launch.ps1 -Monitor

# Skip build (if already built)
.\scripts\demo-launch.ps1 -SkipBuild
```

### **Background Job Startup (For Development)**
```powershell
# Start all services as background jobs
.\scripts\start-monitored.ps1

# Verify everything is working
.\scripts\verify-e2e.ps1

# Check for errors
.\scripts\check-job-errors.ps1

# Monitor continuously
.\scripts\continuous-monitor.ps1
```

### **Legacy Startup (Windows Terminal Tabs)**
```powershell
# Opens separate terminal windows
.\scripts\start-services.ps1
```

---

## 🚀 Quick Start

### 1. Start All Services
```powershell
# RECOMMENDED: One-command launcher
.\scripts\demo-launch.ps1

**What it does:**
- Verifies prerequisites (kubectl, kind cluster, MongoDB, Redis)y
- Builds services in Release mode
- Starts port-forwarding for MongoDB and Redis
- Launches all 4 services (Gateway, Orchestrator, Builder, Runner)
- **Gateway auto-flushes Redis streams on startup** (clean state!)

**Wait ~10 seconds** for services to initialize before testing.

---

### 2. Monitor Logs in Real-Time
```powershell
# Watch all services
.\scripts\monitor-logs.ps1

# Watch specific service
.\scripts\monitor-logs.ps1 -Service Gateway

# Filter by log level
.\scripts\monitor-logs.ps1 -Level error

# Search for specific term
.\scripts\monitor-logs.ps1 -Search "RunId=abc123"
```

**Features:**
- Color-coded output (errors=red, warnings=yellow, success=green)
- Service names highlighted
- Timestamps on every line
- Live tail (updates as logs arrive)

---

### 3. Check for Errors Automatically
```powershell
# Scan last 50 lines of each service
.\scripts\check-errors.ps1

# Scan more lines
.\scripts\check-errors.ps1 -Last 200

# Show full stack traces
.\scripts\check-errors.ps1 -Detailed

# Continuously monitor (refreshes every 5s)
.\scripts\check-errors.ps1 -Watch
```

**What it detects:**
- 🔴 **Protobuf deserialization errors** (corrupted Redis messages)
- ❌ **Exceptions** with stack traces
- ⚠️ **Race conditions** (expected behavior, system auto-retries)
- ⚠️ **General warnings**

**Provides actionable fixes:**
```
🔧 RECOMMENDED FIX:
   1. Stop all services (Ctrl+C in each terminal)
   2. Restart: .\scripts\start-services.ps1
   3. Gateway will auto-flush Redis on startup
```

---

### 4. Generate AI-Ready Debug Report
```powershell
# Monitor for 30 seconds, then generate report
.\scripts\ai-debug.ps1

# Custom duration
.\scripts\ai-debug.ps1 -Duration 60

# Custom output file
.\scripts\ai-debug.ps1 -OutputFile "bug-report.md"
```

**What it creates:**
- Markdown file with all errors, warnings, and context
- Categorized by service
- Full stack traces included
- **Ready to copy-paste to AI assistants** (GitHub Copilot, ChatGPT, etc.)

**Example output:** `debug-report.md`
```markdown
# RepoRunner Debug Report
**Generated:** 2025-10-25 14:30:22

## Summary
### ❌ Builder
- Errors: 2
- Protobuf Issues: 1
- Exceptions: 0

## Detailed Issues
### 🔴 Builder
#### Protobuf Deserialization Errors
**Issue:** Corrupted Redis messages
**Fix:** Restart Gateway (auto-flushes on startup)
```

---

### 5. Stop All Services
```powershell
# Graceful shutdown
.\scripts\stop-services.ps1

# Force kill
.\scripts\stop-services.ps1 -Force
```

**What it does:**
- Stops all service jobs gracefully (sends Ctrl+C)
- Stops port-forwarding jobs
- Cleans up background jobs
- **Gateway auto-cleanup runs on shutdown** (deletes streams)

---

## 🔍 Common Workflows

### Scenario 1: First-time Setup
```powershell
# Start services
.\scripts\start-services.ps1

# Wait 10 seconds, then check for errors
Start-Sleep -Seconds 10
.\scripts\check-errors.ps1

# If no errors, proceed with testing
```

### Scenario 2: Debugging Issues
```powershell
# Start services
.\scripts\start-services.ps1

# Open 2nd terminal: Monitor live logs
.\scripts\monitor-logs.ps1

# Open 3rd terminal: Watch for errors
.\scripts\check-errors.ps1 -Watch

# Trigger your test (e.g., click "Run Locally" in extension)
# Errors appear in real-time in both terminals
```

### Scenario 3: Reporting Bug to AI
```powershell
# Start monitoring
.\scripts\ai-debug.ps1 -Duration 60

# While it monitors, trigger the bug
# Wait for report to generate

# Open debug-report.md
code debug-report.md

# Copy entire file content and paste to AI:
# "Analyze these logs and identify root cause with specific fixes"
```

### Scenario 4: Clean Restart After Changes
```powershell
# Stop everything
.\scripts\stop-services.ps1

# Rebuild
dotnet build -c Release

# Start fresh (auto-flushes Redis)
.\scripts\start-services.ps1
```

---

## 📊 Log Files

All logs saved to: `./logs/`

- `gateway.log` - Gateway service (REST/gRPC API)
- `orchestrator.log` - Orchestrator service (Run orchestration)
- `builder.log` - Builder service (Docker image builds)
- `runner.log` - Runner service (Kubernetes deployments)

**Logs persist** until you delete them or restart with clean:
```powershell
Remove-Item ./logs -Recurse
```

---

## 🎨 Color Coding

### Service Names
- **Gateway** = Magenta
- **Orchestrator** = Blue
- **Builder** = Cyan
- **Runner** = Green

### Log Levels
- **Info** = Cyan/White
- **Warning** = Yellow
- **Error** = Red
- **Success** = Green

---

## 🧹 Auto-Cleanup Features

### On Startup (Gateway Only)
```
🧹 FLUSHING Redis streams on startup to ensure clean state...
✅ Flushed Redis streams: repo-runs=True, indexing=True, dlq=False
```

**Why:** Prevents corrupted message retries from previous runs.

### On Shutdown (All Services)
```
Cleaned up Redis streams on shutdown
```

**Why:** Ensures clean state for next startup.

---

## 💡 Tips

1. **Always wait 10 seconds** after starting services before testing
2. **Use `-Watch` flags** for long-running monitoring
3. **Save debug reports** before restarting services
4. **Gateway must start first** (it flushes Redis)
5. **Check logs directory** if scripts report "no logs found"

---

## 🐛 Troubleshooting

### "No service jobs found"
Services might be running in separate windows (not as jobs).
- Close all service windows manually
- Use `.\scripts\start-services.ps1` to start as jobs

### "Log file not found"
Services haven't started yet or crashed immediately.
- Check if services are running: `Get-Job`
- Check service windows for startup errors

### "Protobuf errors persist"
Old services still running with old builds.
- `.\scripts\stop-services.ps1 -Force`
- Wait 5 seconds
- `.\scripts\start-services.ps1`

---

## 📚 Script Reference

### Core Scripts

| Script | Purpose | Key Flags |
|--------|---------|-----------|
| `demo-launch.ps1` | **One-command demo starter** | `-Monitor`, `-SkipBuild` |
| `start-monitored.ps1` | Start as background jobs | N/A |
| `start-services.ps1` | Start in terminal windows | N/A |
| `stop-services.ps1` | Stop all services | `-Force` |
| `verify-e2e.ps1` | **Full system verification** | N/A |

### Monitoring Scripts

| Script | Purpose | Key Flags |
|--------|---------|-----------|
| `continuous-monitor.ps1` | **Auto-refresh dashboard** | `-CheckIntervalSeconds`, `-AutoRestart` |
| `monitor-jobs.ps1` | Real-time job output | N/A |
| `check-job-errors.ps1` | **Scan jobs for errors** | N/A |
| `monitor-logs.ps1` | Real-time log viewer | `-Service`, `-Level`, `-Search` |
| `check-errors.ps1` | Automatic error detection | `-Last`, `-Detailed`, `-Watch` |

### Utility Scripts

| Script | Purpose | Key Flags |
|--------|---------|-----------|
| `ai-debug.ps1` | Generate AI-ready report | `-Duration`, `-OutputFile` |
| `flush-redis.ps1` | Clear Redis streams/cache | N/A |
| `quick-check.ps1` | Fast health check | N/A |
| `validate-all.ps1` | Comprehensive validation | N/A |
| `ack.ps1` / `ack.sh` | Manual action confirmation | `--step` |

---

## 🚀 For AI Assistants

When debugging with AI, use this workflow:

1. Run: `.\scripts\ai-debug.ps1 -Duration 60`
2. Trigger the bug during monitoring
3. Copy **entire** `debug-report.md` content
4. Paste to AI with prompt:
   > "Analyze these service logs and identify the root cause. Provide specific fixes with file paths and code changes."

The report includes:
- Categorized errors by service
- Full stack traces
- Context lines around errors
- Timestamps and counts
