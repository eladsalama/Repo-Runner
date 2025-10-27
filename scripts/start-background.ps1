#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# Set KUBECONFIG
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

Write-Host "`nStarting RepoRunner Services..." -ForegroundColor Cyan

# Clean up old jobs
Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') } | Stop-Job -PassThru | Remove-Job -ErrorAction SilentlyContinue
Get-Job | Where-Object { $_.Name -like 'PortForward*' } | Stop-Job -PassThru | Remove-Job -ErrorAction SilentlyContinue

# Start port forwarding
Write-Host "[PORT-FORWARD] Starting..." -ForegroundColor Yellow
Start-Job -Name "PortForwardMongo" -ScriptBlock {
    param($kubeconfig)
    $env:KUBECONFIG = $kubeconfig
    kubectl port-forward -n infra svc/mongodb 27017:27017 2>&1
} -ArgumentList $env:KUBECONFIG | Out-Null

Start-Job -Name "PortForwardRedis" -ScriptBlock {
    param($kubeconfig)
    $env:KUBECONFIG = $kubeconfig
    kubectl port-forward -n infra svc/redis-master 6379:6379 2>&1
} -ArgumentList $env:KUBECONFIG | Out-Null

Start-Sleep -Seconds 3
Write-Host "[PORT-FORWARD] Active" -ForegroundColor Green

# Service definitions
$services = @(
    @{Name="Gateway"; Path="src\Gateway"}
    @{Name="Orchestrator"; Path="src\Orchestrator"}
    @{Name="Builder"; Path="src\Builder"}
    @{Name="Runner"; Path="src\Runner"}
)

# Start all services
foreach ($svc in $services) {
    Write-Host "[STARTING] $($svc.Name)..." -ForegroundColor Cyan
    
    $servicePath = Join-Path $repoRoot $svc.Path
    
    Start-Job -Name $svc.Name -ScriptBlock {
        param($workingDir, $kubeconfig)
        
        $env:KUBECONFIG = $kubeconfig
        $env:DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER = "0"
        
        Set-Location $workingDir
        dotnet run -c Release --no-build 2>&1
        
    } -ArgumentList $servicePath, $env:KUBECONFIG | Out-Null
    
    Start-Sleep -Milliseconds 500
}

Write-Host "`n[INIT] Waiting for services..." -ForegroundColor Yellow
Start-Sleep -Seconds 8

Write-Host "`nAll services started!" -ForegroundColor Green
Write-Host "`nStatus:" -ForegroundColor Cyan
Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') } | Format-Table -Property Name, State

Write-Host "`nCommands:" -ForegroundColor Cyan
Write-Host "  View jobs:   Get-Job" -ForegroundColor Gray
Write-Host "  Stop all:    .\scripts\stop-services.ps1" -ForegroundColor Gray
Write-Host ""
