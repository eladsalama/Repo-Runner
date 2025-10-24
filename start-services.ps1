#!/usr/bin/env pwsh
# Start all RepoRunner services with automatic port forwarding

Write-Host "🚀 Starting RepoRunner Services..." -ForegroundColor Cyan

# Check if infrastructure is running
Write-Host "`n📋 Checking infrastructure..." -ForegroundColor Yellow
$mongodbPod = kubectl get pods -n infra -l app.kubernetes.io/name=mongodb -o jsonpath='{.items[0].metadata.name}' 2>$null
$redisPod = kubectl get pods -n infra -l app.kubernetes.io/name=redis -o jsonpath='{.items[0].metadata.name}' 2>$null

if (-not $mongodbPod -or -not $redisPod) {
    Write-Host "❌ Infrastructure not running. Please run:" -ForegroundColor Red
    Write-Host "   cd infra; .\bootstrap.ps1 apply" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Infrastructure is running" -ForegroundColor Green

# Start port forwarding in background jobs
Write-Host "`n🔌 Starting port forwarding..." -ForegroundColor Yellow

# Kill any existing port forwards
Get-Job | Where-Object { $_.Name -like "PortForward*" } | Stop-Job | Remove-Job

$mongoJob = Start-Job -Name "PortForwardMongo" -ScriptBlock {
    kubectl port-forward -n infra svc/mongodb 27017:27017
}

$redisJob = Start-Job -Name "PortForwardRedis" -ScriptBlock {
    kubectl port-forward -n infra svc/redis-master 6379:6379
}

Start-Sleep -Seconds 3
Write-Host "✅ Port forwarding active (MongoDB:27017, Redis:6379)" -ForegroundColor Green

# Start services in separate windows
Write-Host "`n🎯 Starting services in new windows..." -ForegroundColor Yellow

$services = @("Gateway", "Orchestrator", "Builder", "Runner")

foreach ($service in $services) {
    $title = "RepoRunner - $service"
    $command = "cd '$PSScriptRoot\src\$service'; dotnet run"
    
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", $command -WindowStyle Normal
    Write-Host "  ✅ Started $service" -ForegroundColor Green
    Start-Sleep -Milliseconds 500
}

Write-Host "`n✅ All services started!" -ForegroundColor Green
Write-Host "`n📝 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Wait ~10s for services to initialize"
Write-Host "  2. Load extension from ./extension/dist"
Write-Host "  3. Visit any GitHub repo with Dockerfile"
Write-Host "`n⚠️  Port forwarding jobs running in background" -ForegroundColor Yellow
Write-Host "    To stop: Get-Job | Where-Object { `$_.Name -like 'PortForward*' } | Stop-Job | Remove-Job" -ForegroundColor Gray
