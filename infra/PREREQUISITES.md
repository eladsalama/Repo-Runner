# Prerequisites Installation Guide

Quick reference for installing required tools on Windows.

## 1. Docker Desktop

**Download:** https://docs.docker.com/desktop/install/windows-install/

**Installation:**
1. Download Docker Desktop for Windows
2. Run the installer
3. Follow the installation wizard
4. Restart your computer
5. Launch Docker Desktop
6. Ensure "Use WSL 2 based engine" is checked (Settings → General)

**Recommended Settings:**
- **Resources → Memory:** 8GB minimum
- **Resources → CPUs:** 4 CPUs minimum
- **Resources → Disk:** 50GB+

**Verify:**
```powershell
docker version
docker ps
```

## 2. Chocolatey (Package Manager)

**Optional but recommended** for easier tool installation.

**Install:** Open PowerShell as Administrator and run:
```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
```

**Verify:**
```powershell
choco --version
```

## 3. kubectl

**Option A: Via Chocolatey**
```powershell
choco install kubernetes-cli
```

**Option B: Manual Download**
1. Download: https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/
2. Add to PATH

**Verify:**
```powershell
kubectl version --client
```

## 4. Helm

**Option A: Via Chocolatey**
```powershell
choco install kubernetes-helm
```

**Option B: Manual Download**
1. Download: https://github.com/helm/helm/releases
2. Extract to `C:\Program Files\helm\`
3. Add to PATH

**Verify:**
```powershell
helm version
```

## 5. Terraform

**Option A: Via Chocolatey**
```powershell
choco install terraform
```

**Option B: Manual Download**
1. Download: https://www.terraform.io/downloads
2. Extract to `C:\Program Files\terraform\`
3. Add to PATH

**Verify:**
```powershell
terraform version
```

## 6. kind CLI

**Option A: Via Chocolatey**
```powershell
choco install kind
```

**Option B: Manual Download**
```powershell
# Download using PowerShell
curl.exe -Lo kind-windows-amd64.exe https://kind.sigs.k8s.io/dl/v0.20.0/kind-windows-amd64
Move-Item .\kind-windows-amd64.exe C:\Windows\System32\kind.exe
```

**Verify:**
```powershell
kind version
```

## Quick Install (All at Once via Chocolatey)

If you have Chocolatey installed:

```powershell
# Open PowerShell as Administrator
choco install kubernetes-cli kubernetes-helm terraform kind -y
```

## Verify All Tools

Run this script to check everything is installed:

```powershell
$tools = @(
    @{Name="Docker"; Command="docker version"},
    @{Name="kubectl"; Command="kubectl version --client"},
    @{Name="Helm"; Command="helm version"},
    @{Name="Terraform"; Command="terraform version"},
    @{Name="kind"; Command="kind version"}
)

foreach ($tool in $tools) {
    Write-Host "`nChecking $($tool.Name)..." -ForegroundColor Cyan
    try {
        Invoke-Expression $tool.Command
        Write-Host "✓ $($tool.Name) is installed" -ForegroundColor Green
    }
    catch {
        Write-Host "✗ $($tool.Name) is NOT installed" -ForegroundColor Red
    }
}
```

## Next Steps

Once all tools are installed:

1. Ensure Docker Desktop is running
2. Navigate to `infra/` directory
3. Run: `.\bootstrap.ps1 apply`
4. Wait 5-10 minutes for infrastructure to deploy
5. Verify: `.\bootstrap.ps1 verify`

next steps in QUICKREF.md

## Troubleshooting

### Docker not starting
- Check if virtualization is enabled in BIOS
- Ensure WSL 2 is installed: `wsl --install`
- Update Docker Desktop to latest version

### kubectl not found
- Check PATH: `$env:PATH` should include kubectl location
- Restart terminal after installation

### Permission errors
- Run PowerShell as Administrator for installations
- Check execution policy: `Get-ExecutionPolicy`
- If Restricted, set to RemoteSigned: `Set-ExecutionPolicy RemoteSigned`

### Port conflicts
- Ensure ports 30080, 30081, 30082 are not in use
- Check: `netstat -ano | findstr "30080"`

## Optional Tools

### Git (if not already installed)
```powershell
choco install git
```

### .NET SDK 8.0 (for Milestone 2+)
```powershell
choco install dotnet-8.0-sdk
```

### Node.js (for extension development in Milestone 4+)
```powershell
choco install nodejs-lts
```

### VS Code (recommended IDE)
```powershell
choco install vscode
```

## Resource Requirements

**Minimum:**
- Windows 10/11 Pro (for Docker Desktop)
- 16GB RAM (8GB for Docker + 8GB for OS)
- 4 CPU cores
- 50GB free disk space
- Internet connection for initial setup

**Recommended:**
- 32GB RAM
- 8 CPU cores
- SSD with 100GB+ free space
- Fast internet (for downloading images)
