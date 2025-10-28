#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

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
        
        Write-Host "[RESTART] $Name (${Service}:${Port})" -ForegroundColor Yellow
        
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

Write-Host "[CHECK] Infrastructure port-forwards..." -ForegroundColor Cyan

$mongoRestarted = Ensure-PortForward -Name "PortForwardMongo" -Service "mongodb" -Port 27017
$redisRestarted = Ensure-PortForward -Name "PortForwardRedis" -Service "redis-master" -Port 6379

if ($mongoRestarted -or $redisRestarted) {
    Write-Host "[OK] Port-forwards restarted" -ForegroundColor Green
} else {
    Write-Host "[OK] All port-forwards running" -ForegroundColor Green
}

# Show status
Get-Job -Name "PortForward*" | Format-Table -Property Name, State
