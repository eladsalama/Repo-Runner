#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Stops all RepoRunner background services
#>

Write-Host "`nStopping RepoRunner Services..." -ForegroundColor Cyan

# Stop service jobs
$serviceJobs = Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') }

if ($serviceJobs.Count -eq 0) {
    Write-Host "  No running services found" -ForegroundColor DarkGray
} else {
    foreach ($job in $serviceJobs) {
        Write-Host "  [STOP] Stopping $($job.Name)..." -ForegroundColor Yellow
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  Services stopped" -ForegroundColor Green
}

# Stop port-forward jobs
$portForwardJobs = Get-Job | Where-Object { $_.Name -like 'PortForward*' }
if ($portForwardJobs.Count -gt 0) {
    Write-Host "`nStopping port-forwards..." -ForegroundColor Yellow
    foreach ($job in $portForwardJobs) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  Port-forwards stopped" -ForegroundColor Green
}

# Kill any kubectl port-forward processes
$kubectlProcesses = Get-Process | Where-Object { $_.ProcessName -eq 'kubectl' -and $_.CommandLine -like '*port-forward*' }
if ($kubectlProcesses) {
    Write-Host "`nCleaning up kubectl processes..." -ForegroundColor Yellow
    $kubectlProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleanup complete" -ForegroundColor Green
}

# Also kill any orphaned dotnet processes
$dotnetProcesses = Get-Process -Name "Gateway","Orchestrator","Builder","Runner" -ErrorAction SilentlyContinue
if ($dotnetProcesses) {
    Write-Host "`nCleaning up orphaned processes..." -ForegroundColor Yellow
    Stop-Process -Name "Gateway","Orchestrator","Builder","Runner" -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleanup complete" -ForegroundColor Green
}

Write-Host "`nAll services stopped!" -ForegroundColor Green
Write-Host ""
