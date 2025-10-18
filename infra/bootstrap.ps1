param(
  [Parameter(Position=0)]
  [ValidateSet('apply','destroy','status','reset','verify')]
  [string]$Action = 'apply',

  # Optional explicit Terraform directory override
  [string]$TfDir = $null
)

# Relaunch with -NoProfile -ExecutionPolicy Bypass to avoid profile noise/blocked policy
if (-not $env:RR_PS_Relaunched) {
  $env:RR_PS_Relaunched = "1"
  $self = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
  $ps = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  if (-not (Test-Path $ps)) { $ps = "powershell.exe" }
  $argsToPass = @('-NoProfile','-ExecutionPolicy','Bypass','-File', $self) + $args
  Start-Process -FilePath $ps -ArgumentList $argsToPass -Wait -NoNewWindow
  exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Minimal console helpers
function Step([string]$m){ Write-Host ("[>] " + $m) }
function OK  ([string]$m="OK"){ Write-Host ("[OK] " + $m) -ForegroundColor Green }
function FAIL([string]$m){ Write-Host ("[FAIL] " + $m) -ForegroundColor Red }

# Root + logs
$RootDir  = $PSScriptRoot
$LogsBase = Join-Path $RootDir "logs"
if (-not (Test-Path $LogsBase)) { New-Item -ItemType Directory -Force -Path $LogsBase | Out-Null }
$Stamp    = Get-Date -Format "yyyyMMdd_HHmmss"
$RunDir   = Join-Path $LogsBase $Stamp
New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

# Log files
$Log_Prereq   = Join-Path $RunDir "prereq.log"
$Log_Init     = Join-Path $RunDir "terraform_init.log"
$Log_Apply    = Join-Path $RunDir "terraform_apply.log"
$Log_Destroy  = Join-Path $RunDir "terraform_destroy.log"
$Log_KindDel  = Join-Path $RunDir "kind_delete.log"
$Log_PrePull  = Join-Path $RunDir "prepull.log"
$Log_GetNodes = Join-Path $RunDir "kubectl_get_nodes.log"
$Log_Pods0    = Join-Path $RunDir "pods_initial.log"
$Log_PodsTO   = Join-Path $RunDir "pods_timeout.log"

# Quiet runner that writes stdout/stderr to a single log file
function Run-Quiet {
  param(
    [Parameter(Mandatory=$true)][string]$Cmd,
    [string[]]$Args = @(),
    [Parameter(Mandatory=$true)][string]$Log
  )
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $Cmd
  $psi.Arguments = [string]::Join(' ', ($Args | ForEach-Object {
    if ($_ -match '\s') { '"' + ($_ -replace '"','\"') + '"' } else { $_ }
  }))
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $p = New-Object System.Diagnostics.Process
  $p.StartInfo = $psi
  [void]$p.Start()
  $out = $p.StandardOutput.ReadToEnd()
  $err = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  $out | Out-File -FilePath $Log -Encoding UTF8
  if ($err) { $err | Out-File -FilePath $Log -Append -Encoding UTF8 }
  return $p.ExitCode
}

# Resolve Terraform directory (simple + robust)
function Resolve-TerraformDir {
  param([string]$Override)

  # 1) explicit param
  if ($Override -and (Test-Path $Override)) {
    if (@(Get-ChildItem -Path $Override -Filter *.tf -File -ErrorAction SilentlyContinue).Count -gt 0) {
      return (Resolve-Path $Override).Path
    }
  }

  # 2) env var
  if ($env:RR_TF_DIR -and (Test-Path $env:RR_TF_DIR)) {
    if (@(Get-ChildItem -Path $env:RR_TF_DIR -Filter *.tf -File -ErrorAction SilentlyContinue).Count -gt 0) {
      return (Resolve-Path $env:RR_TF_DIR).Path
    }
  }

  # 3) ./terraform under this script
  $t = Join-Path $RootDir "terraform"
  if (Test-Path $t) {
    if (@(Get-ChildItem -Path $t -Filter *.tf -File -ErrorAction SilentlyContinue).Count -gt 0) {
      return (Resolve-Path $t).Path
    }
  }

  # 4) the script folder itself if it contains *.tf
  if (@(Get-ChildItem -Path $RootDir -Filter *.tf -File -ErrorAction SilentlyContinue).Count -gt 0) {
    return (Resolve-Path $RootDir).Path
  }

  return $null
}

$ResolvedTfDir = Resolve-TerraformDir -Override $TfDir
if (-not $ResolvedTfDir) {
  FAIL "No Terraform configuration found."
  Write-Host "Looked in:"
  Write-Host "  - -TfDir (param)"
  Write-Host "  - RR_TF_DIR (env var)"
  Write-Host "  - $($RootDir)\terraform"
  Write-Host "  - $RootDir"
  Write-Host "Tip: run with:  .\bootstrap.ps1 apply -TfDir `"$RootDir\terraform`""
  exit 1
}
$KubeconfigPath = Join-Path $ResolvedTfDir "kubeconfig"

function Check-Prerequisites {
  Step "Checking prerequisites..."
  $ok = $true
  $checks = @(
    @{ n="Docker";    c="docker";    a=@("version") },
    @{ n="kubectl";   c="kubectl";   a=@("version","--client") },
    @{ n="Helm";      c="helm";      a=@("version") },
    @{ n="Terraform"; c="terraform"; a=@("version") },
    @{ n="kind";      c="kind";      a=@("version") }
  )
  foreach ($t in $checks) {
    if (-not (Get-Command $t.c -ErrorAction SilentlyContinue)) {
      $ok = $false
      ("MISSING: {0}" -f $t.c) | Out-File -FilePath $Log_Prereq -Append
    } else {
      [void](Run-Quiet -Cmd $t.c -Args $t.a -Log $Log_Prereq)
    }
  }
  try { docker ps | Out-Null } catch { $ok = $false; "Docker daemon not running" | Out-File -FilePath $Log_Prereq -Append }
  if ($ok) { OK } else { FAIL "Missing tools; see $Log_Prereq"; exit 1 }
}

function Wait-PodsReady {
  param([int]$TimeoutSec = 600, [string]$Namespace = "infra")
  $elapsed = 0; $interval = 3
  while ($elapsed -lt $TimeoutSec) {
    Start-Sleep -Seconds $interval
    $elapsed += $interval
    $podsJson = kubectl get pods -n $Namespace -o json 2>$null
    if ($podsJson) {
      $items = ($podsJson | ConvertFrom-Json).items
      $total = $items.Count; $ready = 0
      foreach ($p in $items) {
        $cs = $p.status.containerStatuses
        if ($null -ne $cs -and ($cs | Where-Object { -not $_.ready }).Count -eq 0) { $ready++ }
      }
      if ($total -gt 0 -and $ready -eq $total) { return $true }
    }
  }
  return $false
}

function Safe-Cleanup([string]$reason) {
  FAIL $reason
  Step "Cleanup (best-effort)..."
  try {
    $clusters = ""
    try { $clusters = kind get clusters 2>&1 | Out-String } catch {}
    if ($clusters -match "reporunner") {
      # Best-effort terraform destroy (with -chdir)
      if (Test-Path $ResolvedTfDir) {
        [void](Run-Quiet -Cmd "terraform" -Args @("-chdir=$ResolvedTfDir","destroy","-auto-approve","-no-color") -Log $Log_Destroy)
      }
      [void](Run-Quiet -Cmd "kind" -Args @("delete","cluster","--name","reporunner") -Log $Log_KindDel)
    }
  } catch {}
  OK "Cleanup complete"
  Write-Host ("Logs: " + $RunDir)
  exit 1
}

function Apply-Infrastructure {
  Check-Prerequisites
  Step ("Terraform dir: " + $ResolvedTfDir); OK

  Step "Reset existing kind cluster (if any)..."
  $clusters = ""
  try { $clusters = kind get clusters 2>&1 | Out-String } catch {}
  if ($clusters -match "reporunner") {
    [void](Run-Quiet -Cmd "kind" -Args @("delete","cluster","--name","reporunner") -Log $Log_KindDel)
  }
  OK

  # Optional pre-pull for speed
  $prePull = Join-Path $RootDir "pre-pull-images.ps1"
  if (Test-Path $prePull) {
    Step "Pre-pulling images..."
    try { & "$prePull" 2>&1 | Out-File -FilePath $Log_PrePull -Encoding UTF8; OK } catch { FAIL "pre-pull failed (see $Log_PrePull)" }
  }

  Step "terraform init..."
  $code = Run-Quiet -Cmd "terraform" -Args @("-chdir=$ResolvedTfDir","init","-input=false","-no-color","-upgrade") -Log $Log_Init
  if ($code -ne 0) { Safe-Cleanup "terraform init failed (see $Log_Init)" } else { OK }

  Step "terraform apply..."
  $code = Run-Quiet -Cmd "terraform" -Args @("-chdir=$ResolvedTfDir","apply","-auto-approve","-no-color","-input=false","-lock-timeout=60s") -Log $Log_Apply
  if ($code -ne 0) { Safe-Cleanup "terraform apply failed (see $Log_Apply)" } else { OK }

  if (-not (Test-Path $KubeconfigPath)) { Safe-Cleanup "kubeconfig not found at $KubeconfigPath" }
  $env:KUBECONFIG = $KubeconfigPath

  Step "Verify API server..."
  try { & kubectl get nodes | Out-File -FilePath $Log_GetNodes -Encoding UTF8; OK } catch { Safe-Cleanup "kubectl cannot reach API server (see $Log_GetNodes)" }

  Step "Wait for pods (infra)..."
  kubectl get pods -n infra | Out-File -FilePath $Log_Pods0 -Encoding UTF8
  if (-not (Wait-PodsReady -TimeoutSec 600 -Namespace "infra")) {
    kubectl get pods -n infra -o wide | Out-File -FilePath $Log_PodsTO -Encoding UTF8
    FAIL "pods not ready in time (cluster kept). See logs."
    Write-Host ("Logs: " + $RunDir)
    exit 2
  } else { OK }

  Write-Host ""
  Write-Host "Done. Cluster is running." -ForegroundColor Green
  Write-Host ("Logs: " + $RunDir)
}

function Destroy-Infrastructure {
  Check-Prerequisites
  Step ("Terraform dir: " + $ResolvedTfDir); OK

  Step "terraform destroy..."
  $code = Run-Quiet -Cmd "terraform" -Args @("-chdir=$ResolvedTfDir","destroy","-auto-approve","-no-color") -Log $Log_Destroy
  if ($code -ne 0) { FAIL "terraform destroy failed (see $Log_Destroy)" } else { OK }

  Step "Delete kind cluster..."
  $code = Run-Quiet -Cmd "kind" -Args @("delete","cluster","--name","reporunner") -Log $Log_KindDel
  if ($code -ne 0) { FAIL "kind delete failed (see $Log_KindDel)" } else { OK }

  Write-Host ("Logs: " + $RunDir)
}

function Show-Status {
  Check-Prerequisites
  Step "kind clusters..."
  try { kind get clusters 2>&1 | Out-File -FilePath (Join-Path $RunDir "status_kind.log") -Encoding UTF8; OK } catch { FAIL }
  Step "pods (infra)..."
  try { kubectl get pods -n infra 2>&1 | Out-File -FilePath (Join-Path $RunDir "status_pods.log") -Encoding UTF8; OK } catch { FAIL }
  Write-Host ("Logs: " + $RunDir)
}

function Reset-Infrastructure {
  Check-Prerequisites
  Step "Delete kind cluster..."
  [void](Run-Quiet -Cmd "kind" -Args @("delete","cluster","--name","reporunner") -Log $Log_KindDel)
  OK
  if (Test-Path $ResolvedTfDir) {
    Step "Reset terraform state..."
    $tfDir = $ResolvedTfDir
    $tfState = Join-Path $tfDir "terraform.tfstate"
    if (Test-Path (Join-Path $tfDir ".terraform")) { Remove-Item -Recurse -Force (Join-Path $tfDir ".terraform") }
    Get-ChildItem -Path $tfDir -Filter "terraform.tfstate*" | ForEach-Object { Remove-Item -Force $_.FullName }
    OK
  }
  Write-Host ("Logs: " + $RunDir)
}

function Verify-Deployment {
  Check-Prerequisites
  Step "verify: kind cluster"
  $clusters = ""
  try { $clusters = kind get clusters 2>&1 | Out-String } catch {}
  if ($clusters -notmatch "reporunner") { FAIL "cluster not found"; return }
  OK "cluster exists"
  Step "pods (infra)..."
  try { kubectl get pods -n infra 2>&1 | Out-File -FilePath (Join-Path $RunDir "verify_pods.log") -Encoding UTF8; OK } catch { FAIL }
  Write-Host ("Logs: " + $RunDir)
}

switch ($Action) {
  'apply'   { Apply-Infrastructure }
  'destroy' { Destroy-Infrastructure }
  'status'  { Show-Status }
  'reset'   { Reset-Infrastructure }
  'verify'  { Verify-Deployment }
  default   { Apply-Infrastructure }
}
