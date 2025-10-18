# bootstrap.ps1
# Usage: .\bootstrap.ps1 [apply|destroy|status|reset|verify]

param(
  [Parameter(Position=0)]
  [ValidateSet('apply','destroy','status','reset','verify')]
  [string]$Action = 'apply'
)

# -------------------------
# Self-relaunch once with safer flags
# -------------------------
if (-not $env:RR_PS_Relaunched) {
  $env:RR_PS_Relaunched = "1"
  $self = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
  $passArgs = @()
  if ($MyInvocation.UnboundArguments) { $passArgs += $MyInvocation.UnboundArguments }
  if ($args.Count -gt 0 -and -not $passArgs) { $passArgs += $args }
  $ps = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  if (-not (Test-Path $ps)) { $ps = "powershell.exe" }
  $psArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File', $self) + $passArgs
  Start-Process -FilePath $ps -ArgumentList $psArgs -Wait -NoNewWindow
  exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Info([string]$m){ Write-Host "[*] $m" -ForegroundColor Cyan }
function Ok  ([string]$m){ Write-Host "[OK] $m" -ForegroundColor Green }
function Err ([string]$m){ Write-Host "[ERR] $m" -ForegroundColor Red }

# Refresh PATH
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path","User")

# Common paths
$TfDir = Join-Path $PSScriptRoot "terraform"
$KubeconfigPath = Join-Path $TfDir "kubeconfig"

function Set-KubeEnv {
  # Always point kubectl at the Terraform-generated kubeconfig
  $env:KUBECONFIG = $KubeconfigPath
}

function Check-Prerequisites {
  Info "Checking prerequisites..."
  $need = @(
    @{n="Docker";    c="docker";    args=@("version")},
    @{n="kubectl";   c="kubectl";   args=@("version","--client")},
    @{n="Helm";      c="helm";      args=@("version")},
    @{n="Terraform"; c="terraform"; args=@("version")},
    @{n="kind";      c="kind";      args=@("version")}
  )
  $ok = $true
  foreach ($t in $need) {
    if (-not (Get-Command $t.c -ErrorAction SilentlyContinue)) {
      Err "$($t.n) not found in PATH"; $ok = $false; continue
    }
    try { & $t.c @($t.args) | Out-Null; Ok "$($t.n) is installed" }
    catch { Err "$($t.n) check failed: $($_.Exception.Message)"; $ok = $false }
  }
  try { docker ps | Out-Null; Ok "Docker daemon is running" }
  catch { Err "Docker daemon is not running"; $ok = $false }
  if (-not $ok) { throw "Missing prerequisites" }
}

function Safe-Cleanup([string]$reason) {
  Err "Deployment failed: $reason"
  Info "Cleaning up..."
  try {
    # Use kubeconfig if present for destroy
    if (Test-Path $KubeconfigPath) { Set-KubeEnv }
    $clusters = kind get clusters 2>&1 | Out-String
    if ($clusters -match "reporunner") {
      try {
        if (Test-Path $TfDir) {
          Push-Location $TfDir
          if (Test-Path "terraform.tfstate") {
            Info "terraform destroy (best-effort)..."
            terraform destroy -auto-approve -no-color 2>&1 | Out-Null
          }
        }
      } catch {}
      finally { if ((Get-Location).Path -ne $PSScriptRoot) { Pop-Location } }
      Info "Deleting kind cluster..."
      kind delete cluster --name reporunner 2>&1 | Out-Null
    }
  } catch {}
  Ok "Cleanup complete"
  exit 1
}

function Wait-PodsReady {
  param([int]$TimeoutSec = 600)
  Info "Waiting for pods in namespace 'infra'..."
  $elapsed = 0
  $interval = 3
  while ($elapsed -lt $TimeoutSec) {
    Start-Sleep -Seconds $interval
    $elapsed += $interval
    $podsJson = kubectl get pods -n infra -o json 2>$null
    if ($podsJson) {
      $items = ($podsJson | ConvertFrom-Json).items
      $total = $items.Count
      $ready = 0
      foreach ($p in $items) {
        $cs = $p.status.containerStatuses
        if ($null -ne $cs -and ($cs | Where-Object { -not $_.ready }).Count -eq 0) { $ready++ }
      }
      Write-Host ("Pods ready: {0}/{1}  elapsed={2}s" -f $ready, $total, $elapsed)
      if ($total -gt 0 -and $ready -eq $total) { return $true }
    }
  }
  return $false
}

function Apply-Infrastructure {
  Check-Prerequisites

  if (-not (Test-Path $TfDir)) { throw "terraform folder not found at $TfDir" }

  # Clean old cluster each run to avoid context races
  try {
    $existing = kind get clusters 2>&1 | Out-String
    if ($existing -match "reporunner") {
      Info "Deleting existing 'reporunner' cluster..."
      kind delete cluster --name reporunner 2>&1 | Out-Null
    }
  } catch {}

  Push-Location $TfDir
  try {
    Info "terraform init (upgrade)..."
    terraform init -input=false -no-color -upgrade
    if ($LASTEXITCODE -ne 0) { throw "terraform init failed" }

    Info "terraform apply..."
    terraform apply -auto-approve -no-color -input=false -lock-timeout=60s
    if ($LASTEXITCODE -ne 0) { throw "terraform apply failed" }
    Ok "terraform apply finished"

    # Point kubectl at the just-written kubeconfig
    if (-not (Test-Path $KubeconfigPath)) { throw "kubeconfig not found at $KubeconfigPath" }
    Set-KubeEnv

    # Quick sanity checks
    $ctxs = kubectl config get-contexts 2>$null | Out-String
    if ($ctxs -notmatch "kind-reporunner") { throw "kubectl context kind-reporunner not found" }

    $ns = kubectl get ns infra -o jsonpath='{.metadata.name}' 2>$null
    if ($ns -ne "infra") { throw "namespace 'infra' not found" }

    Info "Current pods:"
    kubectl get pods -n infra

    $ready = Wait-PodsReady -TimeoutSec 600
    if ($ready) {
      Ok "All pods ready"
    } else {
      Err "Pods not ready within timeout"
      kubectl get pods -n infra -o wide
      exit 1
    }
  } catch {
    Pop-Location
    Safe-Cleanup $_.Exception.Message
  } finally {
    if ((Get-Location).Path -ne $PSScriptRoot) { Pop-Location }
  }
}

function Destroy-Infrastructure {
  Check-Prerequisites
  # Use kubeconfig if it exists
  if (Test-Path $KubeconfigPath) { Set-KubeEnv }
  Push-Location $TfDir
  try {
    Info "terraform destroy..."
    terraform destroy -auto-approve -no-color
    if ($LASTEXITCODE -ne 0) { throw "terraform destroy failed" }
    Ok "Infrastructure destroyed"
  } catch {
    Pop-Location
    Safe-Cleanup $_.Exception.Message
  } finally {
    if ((Get-Location).Path -ne $PSScriptRoot) { Pop-Location }
  }
}

function Show-Status {
  Check-Prerequisites
  if (Test-Path $KubeconfigPath) { Set-KubeEnv }
  Info "kind clusters:"
  kind get clusters 2>&1
  Info "pods (infra):"
  kubectl get pods -n infra 2>&1
}

function Reset-Infrastructure {
  Check-Prerequisites
  Info "Deleting kind cluster (if any)..."
  kind delete cluster --name reporunner 2>$null | Out-Null
  if (Test-Path $TfDir) {
    Push-Location $TfDir
    try {
      if (Test-Path ".terraform") { Remove-Item -Recurse -Force ".terraform" }
      Get-ChildItem -Filter "terraform.tfstate*" | ForEach-Object { Remove-Item -Force $_.FullName }
      Ok "Terraform state reset"
    } finally { Pop-Location }
  }
  Ok "Reset complete"
}

function Verify-Deployment {
  Check-Prerequisites
  if (Test-Path $KubeconfigPath) { Set-KubeEnv }
  $clusters = kind get clusters 2>&1 | Out-String
  if ($clusters -notmatch "reporunner") { Err "Cluster not found"; return }
  Ok "Cluster exists"
  Info "pods (infra):"
  kubectl get pods -n infra
}

switch ($Action) {
  'apply'   { Apply-Infrastructure }
  'destroy' { Destroy-Infrastructure }
  'status'  { Show-Status }
  'reset'   { Reset-Infrastructure }
  'verify'  { Verify-Deployment }
  default   { Apply-Infrastructure }
}
