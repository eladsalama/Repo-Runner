# RepoRunner Infrastructure Bootstrap Script
# Usage: .\bootstrap.ps1 [apply|destroy|status|reset|verify]

param(
  [Parameter(Position=0)]
  [ValidateSet('apply','destroy','status','reset','verify')]
  [string]$Action = 'apply'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Refresh PATH environment variable to pick up newly installed tools
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# Track if we're in the middle of deployment
$script:DeploymentInProgress = $false
$script:CleanupDone = $false

# Trap to handle script termination (Ctrl+C, unexpected errors)
trap {
  if ($script:DeploymentInProgress -and -not $script:CleanupDone) {
    Write-Host "`n`nScript interrupted! Cleaning up..." -ForegroundColor Yellow
    $script:CleanupDone = $true
    
    # Clean up cluster if it exists
    $clusters = kind get clusters 2>&1 | Out-String
    if ($clusters -match "reporunner") {
      Write-Host "Deleting partially deployed cluster..." -ForegroundColor Yellow
      kind delete cluster --name reporunner 2>&1 | Out-Null
      Write-Host "Cleanup complete." -ForegroundColor Green
    }
  }
  exit 1
}

function Write-Step     { param([string]$m) Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok       { param([string]$m) Write-Host "[OK] $m" -ForegroundColor Green }
function Write-Err      { param([string]$m) Write-Host "[ERR] $m" -ForegroundColor Red }

function Check-Prerequisites {
  Write-Step "Checking prerequisites..."

  $tools = @(
    @{Name="Docker";    Check="docker version"},
    @{Name="kubectl";   Check="kubectl version --client"},
    @{Name="Helm";      Check="helm version"},
    @{Name="Terraform"; Check="terraform version"},
    @{Name="kind";      Check="kind version"}
  )

  $all = $true
  foreach ($t in $tools) {
    try { $null = Invoke-Expression $t.Check 2>&1; Write-Ok "$($t.Name) is installed" }
    catch { Write-Err "$($t.Name) is not installed or not in PATH"; $all = $false }
  }
  if (-not $all) {
    Write-Host ""; Write-Host "Please install missing tools. See infra/README.md." -ForegroundColor Yellow
    exit 1
  }

  try { docker ps | Out-Null; Write-Ok "Docker daemon is running" }
  catch { Write-Err "Docker daemon is not running. Start Docker Desktop."; exit 1 }
}

function Safe-Cleanup {
  param([string]$reason)
  
  $script:CleanupDone = $true
  $script:DeploymentInProgress = $false
  
  Write-Host ""
  Write-Err "Deployment failed: $reason"
  Write-Step "Performing safe cleanup..."
  
  # Check if cluster exists and destroy it
  $clusters = kind get clusters 2>&1 | Out-String
  if ($clusters -match "reporunner") {
    Write-Host "Cleaning up partially deployed cluster..." -ForegroundColor Yellow
    
    Push-Location "$PSScriptRoot\terraform"
    try {
      # Try terraform destroy first
      if (Test-Path "terraform.tfstate") {
        Write-Host "Running terraform destroy..." -ForegroundColor Yellow
        terraform destroy -auto-approve 2>&1 | Out-Null
      }
    } catch {
      Write-Host "Terraform destroy failed, forcing kind cluster deletion..." -ForegroundColor Yellow
    } finally {
      Pop-Location
    }
    
    # Force delete kind cluster
    Write-Host "Deleting kind cluster..." -ForegroundColor Yellow
    kind delete cluster --name reporunner 2>&1 | Out-Null
  } else {
    Write-Host "No cluster to clean up." -ForegroundColor DarkGray
  }
  
  # Clean up any orphaned Docker containers
  Write-Host "Checking for orphaned Docker containers..." -ForegroundColor Yellow
  $containers = docker ps -aq --filter "name=reporunner" 2>&1 | Out-String
  if ($containers -and $containers.Trim() -ne "") {
    Write-Host "Removing Docker containers..." -ForegroundColor Yellow
    docker rm -f $containers.Trim() 2>&1 | Out-Null
  }
  
  Write-Ok "Cleanup complete. Infrastructure stopped safely."
  Write-Host ""
  Write-Host "Note: WSL (VmmemWSL) may stay running for a few minutes - this is normal." -ForegroundColor Cyan
  Write-Host "Fix the issue and run: .\bootstrap.ps1 apply" -ForegroundColor Yellow
  exit 1
}

function Apply-Infrastructure {
  Write-Step "Applying infrastructure..."
  
  # Check for existing cluster and clean state if needed
  $existingClusters = $null
  try {
    $existingClusters = kind get clusters 2>&1 | Out-String
  } catch {
    $existingClusters = ""
  }
  
  if ($existingClusters -match "reporunner") {
    Write-Host "Found existing 'reporunner' cluster. Cleaning up first..." -ForegroundColor Yellow
    kind delete cluster --name reporunner 2>&1 | Out-Null
    Write-Ok "Old cluster removed"
  }
  
  # Clean stale Terraform state if cluster doesn't exist
  if (Test-Path "$PSScriptRoot\terraform\terraform.tfstate") {
    $stateContent = Get-Content "$PSScriptRoot\terraform\terraform.tfstate" -Raw -ErrorAction SilentlyContinue
    if ($stateContent -and $stateContent -match "reporunner" -and -not ($existingClusters -match "reporunner")) {
      Write-Host "Cleaning stale Terraform state..." -ForegroundColor Yellow
      Remove-Item "$PSScriptRoot\terraform\terraform.tfstate*" -Force
      Write-Ok "Stale state cleaned"
    }
  }
  
  $script:DeploymentInProgress = $true

  Push-Location "$PSScriptRoot\terraform"
  try {
    if (-not (Test-Path ".terraform")) { 
      Write-Step "Initializing Terraform..."
      terraform init
      if ($LASTEXITCODE -ne 0) {
        throw "Terraform init failed"
      }
    }

    Write-Step "Creating kind cluster and deploying services..."
    Write-Host "Images are pre-pulled. Deployment should be fast!" -ForegroundColor Green
    
    terraform apply -auto-approve
    
    if ($LASTEXITCODE -ne 0) {
      throw "Terraform apply failed or was interrupted"
    }

    $script:DeploymentInProgress = $false
    
    Write-Ok "Terraform apply completed!"
    Write-Host ""
    Write-Step "Waiting for pods to become ready..."
    Write-Host "  MAXIMUM SPEED MODE - 4x resources" -ForegroundColor Cyan
    Write-Host "  First run: 1-2 min (pulling images)" -ForegroundColor Yellow
    Write-Host "  Subsequent runs: 10-20 sec (images cached)" -ForegroundColor Green
    Write-Host "  Total RAM: ~2.5GB (Redis 256MB + MongoDB 1GB + Kafka 2GB + OTel 256MB)" -ForegroundColor DarkGray
    Write-Host ""
    
    # Wait for pods to be ready - generous timeout for first run
    $timeout = 900  # 15 minutes - plenty of time for image pulls
    $elapsed = 0
    $allReady = $false
    
    while (-not $allReady -and $elapsed -lt $timeout) {
      Start-Sleep -Seconds 3  # Check every 3 seconds for faster feedback
      $elapsed += 3
      
      $podsJson = kubectl get pods -n infra -o json 2>$null
      if ($podsJson) {
        $pods = ($podsJson | ConvertFrom-Json).items
        $readyCount = 0
        $totalCount = $pods.Count
        
        foreach ($pod in $pods) {
          if ($pod.status.phase -eq "Running") {
            $readyCount++
          }
        }
        
        Write-Host "`rPods ready: $readyCount/$totalCount (${elapsed}s elapsed)" -NoNewline
        
        if ($readyCount -eq $totalCount -and $totalCount -gt 0) {
          $allReady = $true
        }
      }
    }
    
    Write-Host ""
    
    if ($allReady) {
      Write-Ok "All pods are ready!"
    } else {
      Write-Host ""
      Write-Host "Pods are taking longer than expected. This usually means:" -ForegroundColor Yellow
      Write-Host "  - Docker is pulling images (first time is slow)" -ForegroundColor White
      Write-Host "  - Insufficient resources allocated to Docker" -ForegroundColor White
      Write-Host ""
      Write-Host "Check status with:" -ForegroundColor Yellow
      Write-Host "  kubectl get pods -n infra" -ForegroundColor White
      Write-Host "  kubectl logs -n infra [pod-name]" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "Services are running in the background. To stop:" -ForegroundColor Yellow
    Write-Host "  .\bootstrap.ps1 destroy" -ForegroundColor White
    Write-Host ""
  } catch {
    Pop-Location
    $script:DeploymentInProgress = $false
    Safe-Cleanup $_.Exception.Message
  } finally {
    if ((Get-Location).Path -ne $PSScriptRoot) {
      Pop-Location
    }
  }
}

function Destroy-Infrastructure {
  Write-Step "Destroying infrastructure..."

  Push-Location "$PSScriptRoot\terraform"
  try {
    terraform destroy -auto-approve
    Write-Ok "Infrastructure destroyed successfully"
    
    # Clean up any orphaned containers
    $containers = docker ps -aq --filter "name=reporunner" 2>&1
    if ($containers -and $containers -ne "") {
      Write-Host "Removing Docker containers..." -ForegroundColor Yellow
      docker rm -f $containers 2>&1 | Out-Null
    }
    
    Write-Host ""
    Write-Host "Note: WSL (VmmemWSL) may stay running - this is normal." -ForegroundColor Cyan
    Write-Host "To force WSL shutdown (will restart Docker Desktop): wsl --shutdown" -ForegroundColor DarkGray
  } finally {
    Pop-Location
  }
}

function Show-Status {
  Write-Step "Checking infrastructure status..."

  $clusters = kind get clusters 2>&1
  if ($clusters -match "reporunner") {
    Write-Ok "kind cluster 'reporunner' is running"

    Write-Step "Pods in namespace 'infra'..."
    kubectl get pods -n infra

    Write-Step "Service endpoints:"
    Write-Host "Jaeger UI:  http://localhost:30082" -ForegroundColor Green
    Write-Host "Preview:    http://localhost:30080" -ForegroundColor Green
  } else {
    Write-Err "kind cluster 'reporunner' is not running"
    Write-Host "Run: .\bootstrap.ps1 apply" -ForegroundColor Yellow
  }
}

function Reset-Infrastructure {
  Write-Step "Performing complete reset..."

  Write-Step "Deleting kind cluster..."
  kind delete cluster --name reporunner 2>$null | Out-Null

  Push-Location "$PSScriptRoot\terraform"
  try {
    if (Test-Path ".terraform") { Remove-Item -Recurse -Force ".terraform" }
    Get-ChildItem -Filter "terraform.tfstate*" | ForEach-Object { Remove-Item -Force $_.FullName }
    Write-Ok "Reset complete"
    Write-Host "Run: .\bootstrap.ps1 apply" -ForegroundColor Yellow
  } finally {
    Pop-Location
  }
}

function Verify-Deployment {
  Write-Step "Verifying deployment..."

  $clusters = kind get clusters 2>&1
  if (-not ($clusters -match "reporunner")) { 
    Write-Err "Cluster not found"
    return $false 
  }
  Write-Ok "Cluster exists"

  Write-Step "Checking pod health..."
  $podsJson = kubectl get pods -n infra -o json 2>$null
  if (-not $podsJson) { 
    Write-Err "Failed to list pods"
    return $false 
  }
  
  $pods = ($podsJson | ConvertFrom-Json).items
  $allRunning = $true
  foreach ($p in $pods) {
    $name = $p.metadata.name
    $phase = $p.status.phase
    if ($phase -eq "Running") { 
      Write-Ok "$name is Running" 
    } else { 
      Write-Err "$name is $phase"
      $allRunning = $false 
    }
  }

  if ($allRunning) {
    Write-Ok "All services are healthy"
    return $true
  } else {
    Write-Err "Some services are not healthy. Check logs: kubectl logs -n infra [pod-name]"
    return $false
  }
}

# Main
Write-Host ""
Write-Host "--------------------------------------------" -ForegroundColor Cyan
Write-Host "|   RepoRunner Infrastructure Bootstrap    |" -ForegroundColor Cyan
Write-Host "--------------------------------------------" -ForegroundColor Cyan
Write-Host ""

Check-Prerequisites

switch ($Action) {
  'apply'   { Apply-Infrastructure; Start-Sleep -Seconds 5; Verify-Deployment | Out-Null }
  'destroy' { Destroy-Infrastructure }
  'status'  { Show-Status }
  'reset'   { Reset-Infrastructure }
  'verify'  { Verify-Deployment | Out-Null }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Cyan
