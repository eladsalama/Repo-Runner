<#
.SYNOPSIS
    Starts all RepoRunner services as background jobs for AI monitoring
.DESCRIPTION
    Launches Gateway, Orchestrator, Builder, and Runner as PowerShell jobs.
    Outputs are captured and can be monitored in real-time by AI.
#>

param(
    [switch]$ShowOutput = $true  # Show live output as services run
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# Set KUBECONFIG for all services
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

Write-Host "🚀 Starting RepoRunner Services (Monitored Mode)..." -ForegroundColor Cyan
Write-Host ""

# Check prerequisites quickly
try {
    $mongoStatus = kubectl get pod -n infra -l app.kubernetes.io/name=mongodb -o jsonpath='{.items[0].status.phase}' 2>$null
    $redisStatus = kubectl get pod -n infra -l app.kubernetes.io/name=redis -o jsonpath='{.items[0].status.phase}' 2>$null
    
    if ($mongoStatus -ne "Running" -or $redisStatus -ne "Running") {
        Write-Host "❌ Infrastructure not ready (MongoDB: $mongoStatus, Redis: $redisStatus)" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Infrastructure ready" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to check infrastructure: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Stop any existing jobs and processes
Write-Host "Cleaning up old jobs and processes..." -ForegroundColor Yellow
Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') } | Stop-Job -PassThru | Remove-Job -ErrorAction SilentlyContinue
Get-Job | Where-Object { $_.Name -like 'PortForward*' } | Stop-Job -PassThru | Remove-Job -ErrorAction SilentlyContinue

# Kill any dotnet processes holding Shared.dll
Get-Process -Name Gateway,Orchestrator,Builder,Runner -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Start port forwarding
Write-Host "🔌 Starting port forwarding..." -ForegroundColor Yellow
$mongoJob = Start-Job -Name "PortForwardMongo" -ScriptBlock {
    kubectl port-forward -n infra svc/mongodb 27017:27017 2>&1
}
$redisJob = Start-Job -Name "PortForwardRedis" -ScriptBlock {
    kubectl port-forward -n infra svc/redis-master 6379:6379 2>&1
}
Start-Sleep -Seconds 3
Write-Host "✅ Port forwarding active" -ForegroundColor Green

# Build services
Write-Host "🔨 Building services..." -ForegroundColor Yellow
Push-Location $repoRoot
$buildOutput = dotnet build -c Release --nologo 2>&1
$buildResult = $LASTEXITCODE
Pop-Location

if ($buildResult -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}
Write-Host "✅ Build succeeded" -ForegroundColor Green
Write-Host ""

# Function to start a service as background job
function Start-ServiceJob {
    param(
        [string]$Name,
        [string]$Path
    )
    
    Write-Host "▶️  Starting $Name..." -ForegroundColor Cyan
    
    $job = Start-Job -Name $Name -ScriptBlock {
        param($workingDir, $kubeconfig, $useSockets)
        
        $env:KUBECONFIG = $kubeconfig
        $env:DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER = $useSockets
        
        Set-Location $workingDir
        
        # Run service and output everything
        dotnet run -c Release --no-build 2>&1
        
    } -ArgumentList $Path, $env:KUBECONFIG, "0"
    
    return $job
}

# Start all services
$services = @(
    @{Name="Gateway"; Path=Join-Path $repoRoot "src\Gateway"}
    @{Name="Orchestrator"; Path=Join-Path $repoRoot "src\Orchestrator"}
    @{Name="Builder"; Path=Join-Path $repoRoot "src\Builder"}
    @{Name="Runner"; Path=Join-Path $repoRoot "src\Runner"}
)

$jobs = @()
foreach ($svc in $services) {
    $job = Start-ServiceJob -Name $svc.Name -Path $svc.Path
    $jobs += @{Name=$svc.Name; Job=$job}
    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "✅ All services started as background jobs!" -ForegroundColor Green
Write-Host ""

# Wait for services to initialize
Write-Host "⏳ Waiting for services to initialize..." -ForegroundColor Yellow

$startTime = Get-Date
$timeout = 30
$allReady = $false

while (((Get-Date) - $startTime).TotalSeconds -lt $timeout) {
    $outputs = @{}
    
    foreach ($svc in $jobs) {
        $output = Receive-Job -Job $svc.Job -Keep 2>&1
        if ($output) {
            $outputs[$svc.Name] = $output -join "`n"
        }
    }
    
    # Check if Gateway shows "Application started"
    $gatewayReady = $outputs["Gateway"] -match "Application started|Now listening"
    $orchestratorReady = $outputs["Orchestrator"] -match "Application started"
    $builderReady = $outputs["Builder"] -match "Builder worker starting"
    $runnerReady = $outputs["Runner"] -match "Runner worker starting"
    
    if ($gatewayReady -and $orchestratorReady -and $builderReady -and $runnerReady) {
        $allReady = $true
        break
    }
    
    Start-Sleep -Milliseconds 500
}

Write-Host ""

if ($allReady) {
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "  ALL SERVICES READY" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Host "Services started but some may still be initializing" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Service Status:" -ForegroundColor Cyan
Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') } | Format-Table -Property Id, Name, State, HasMoreData

Write-Host ""
Write-Host "Monitoring Commands:" -ForegroundColor Cyan
Write-Host "   View live output:  Get-Job -Name Gateway | Receive-Job -Keep" -ForegroundColor Gray
Write-Host "   Monitor all:       .\scripts\monitor-jobs.ps1" -ForegroundColor Gray
Write-Host "   Check errors:      .\scripts\check-job-errors.ps1" -ForegroundColor Gray
Write-Host ""

if ($ShowOutput) {
    Write-Host "==========================================" -ForegroundColor DarkGray
    Write-Host "  INITIAL OUTPUT (last 20 lines per service)" -ForegroundColor White
    Write-Host "==========================================" -ForegroundColor DarkGray
    Write-Host ""
    
    foreach ($svc in $jobs) {
        $output = Receive-Job -Job $svc.Job -Keep 2>&1
        if ($output) {
            $lines = ($output -join "`n") -split "`n"
            $lastLines = $lines | Select-Object -Last 20
            
            Write-Host "[$($svc.Name)]" -ForegroundColor Cyan
            foreach ($line in $lastLines) {
                if ($line -match "error|fail|exception") {
                    Write-Host "  $line" -ForegroundColor Red
                } elseif ($line -match "warn") {
                    Write-Host "  $line" -ForegroundColor Yellow
                } elseif ($line -match "started|ready|listening") {
                    Write-Host "  $line" -ForegroundColor Green
                } else {
                    Write-Host "  $line" -ForegroundColor Gray
                }
            }
            Write-Host ""
        }
    }
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "  Ready for testing! Click Run Locally now" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
