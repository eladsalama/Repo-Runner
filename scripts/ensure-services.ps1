#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Ensures RepoRunner services are running (starts them if not already up)
.DESCRIPTION
    Checks if services are running and starts them in background if needed.
    Returns status for each service.
#>

$ErrorActionPreference = "Stop"

# Service names and ports
$services = @(
    @{ Name = "Gateway"; Port = 5247; Path = "src/Gateway" }
    @{ Name = "Orchestrator"; Port = 5248; Path = "src/Orchestrator" }
    @{ Name = "Builder"; Port = 5249; Path = "src/Builder" }
    @{ Name = "Runner"; Port = 5250; Path = "src/Runner" }
)

$repoRoot = Split-Path -Parent $PSScriptRoot

# Function to check if a service is running on a port
function Test-ServiceRunning {
    param([int]$Port)
    
    try {
        $connection = Test-NetConnection -ComputerName localhost -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
        return $connection
    } catch {
        return $false
    }
}

# Function to start a service in background
function Start-ServiceBackground {
    param(
        [string]$Name,
        [string]$Path
    )
    
    $servicePath = Join-Path $repoRoot $Path
    $env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"
    
    $jobName = "RepoRunner-$Name"
    
    # Check if job already exists
    $existingJob = Get-Job -Name $jobName -ErrorAction SilentlyContinue
    if ($existingJob) {
        Remove-Job -Name $jobName -Force -ErrorAction SilentlyContinue
    }
    
    # Start service as background job
    $job = Start-Job -Name $jobName -ScriptBlock {
        param($ServicePath, $KubeConfig)
        
        $env:KUBECONFIG = $KubeConfig
        if ($ServicePath -like "*Runner*") {
            $env:DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER = '0'
        }
        
        Set-Location $ServicePath
        dotnet run -c Release --no-build 2>&1
    } -ArgumentList $servicePath, $env:KUBECONFIG
    
    return $job
}

Write-Host "Checking RepoRunner services..." -ForegroundColor Cyan

$results = @()
$anyStarted = $false

foreach ($service in $services) {
    $isRunning = Test-ServiceRunning -Port $service.Port
    
    if ($isRunning) {
        Write-Host "  $($service.Name) is running" -ForegroundColor Green
        $results += @{ Service = $service.Name; Status = "running"; Action = "none" }
    } else {
        Write-Host "  Starting $($service.Name)..." -ForegroundColor Yellow
        $job = Start-ServiceBackground -Name $service.Name -Path $service.Path
        $anyStarted = $true
        $results += @{ Service = $service.Name; Status = "starting"; Action = "started"; JobId = $job.Id }
    }
}

if ($anyStarted) {
    Write-Host "`Waiting 5 seconds for services to initialize..." -ForegroundColor Cyan
    Start-Sleep -Seconds 5
    
    # Verify started services
    Write-Host "`n🔍 Verifying started services..." -ForegroundColor Cyan
    foreach ($result in $results) {
        if ($result.Action -eq "started") {
            $service = $services | Where-Object { $_.Name -eq $result.Service } | Select-Object -First 1
            $isRunning = Test-ServiceRunning -Port $service.Port
            
            if ($isRunning) {
                Write-Host "  ✅ $($result.Service) is now running" -ForegroundColor Green
            } else {
                Write-Host "  ⚠️  $($result.Service) may still be initializing (check logs)" -ForegroundColor Yellow
            }
        }
    }
}

Write-Host "`All services are ready!" -ForegroundColor Green

# Output results as JSON for programmatic consumption
$resultsJson = $results | ConvertTo-Json -Compress
Write-Output "RESULTS:$resultsJson"
