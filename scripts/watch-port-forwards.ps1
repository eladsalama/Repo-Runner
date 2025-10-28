#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

Write-Host "[WATCH] Starting port-forward monitor..." -ForegroundColor Cyan
Write-Host "[WATCH] Will check every 10 seconds and auto-restart if needed" -ForegroundColor Gray

$checkInterval = 10

function Ensure-PortForward {
    param(
        [string]$Name,
        [string]$Service,
        [int]$Port,
        [string]$Namespace = "infra"
    )
    
    $job = Get-Job -Name $Name -ErrorAction SilentlyContinue
    
    if ($null -eq $job -or $job.State -ne 'Running') {
        if ($null -ne $job) {
            Remove-Job -Name $Name -Force -ErrorAction SilentlyContinue
        }
        
        $timestamp = Get-Date -Format "HH:mm:ss"
        Write-Host "[$timestamp] [RESTART] $Name (${Service}:${Port})" -ForegroundColor Yellow
        
        Start-Job -Name $Name -ScriptBlock {
            param($kubeconfig, $service, $port, $namespace)
            $env:KUBECONFIG = $kubeconfig
            kubectl port-forward -n $namespace svc/$service ${port}:${port} 2>&1
        } -ArgumentList $env:KUBECONFIG, $Service, $Port, $Namespace | Out-Null
        
        Start-Sleep -Seconds 2
        return $true
    }
    
    return $false
}

# Initial setup
Ensure-PortForward -Name "PortForwardMongo" -Service "mongodb" -Port 27017 | Out-Null
Ensure-PortForward -Name "PortForwardRedis" -Service "redis-master" -Port 6379 | Out-Null

Write-Host "[WATCH] Monitoring active. Press Ctrl+C to stop." -ForegroundColor Green
Write-Host ""

try {
    while ($true) {
        Start-Sleep -Seconds $checkInterval
        
        $mongoRestarted = Ensure-PortForward -Name "PortForwardMongo" -Service "mongodb" -Port 27017
        $redisRestarted = Ensure-PortForward -Name "PortForwardRedis" -Service "redis-master" -Port 6379
        
        if (-not $mongoRestarted -and -not $redisRestarted) {
            # All good, just print a heartbeat every minute
            $script:heartbeatCount++
            if ($script:heartbeatCount % 6 -eq 0) {  # Every 60 seconds
                $timestamp = Get-Date -Format "HH:mm:ss"
                Write-Host "[$timestamp] [OK] All port-forwards running" -ForegroundColor DarkGray
            }
        }
    }
} catch {
    Write-Host "`n[STOP] Monitor stopped" -ForegroundColor Yellow
}
