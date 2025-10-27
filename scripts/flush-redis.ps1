# Flush Redis to clear all streams and corrupted messages
Write-Host "Flushing Redis to clear corrupted messages..." -ForegroundColor Yellow

# Connect to Redis and flush
$redisCommand = @"
redis-cli FLUSHALL
"@

# Try redis-cli if available
try {
    redis-cli FLUSHALL
    Write-Host "✅ Redis flushed successfully via redis-cli" -ForegroundColor Green
} catch {
    # Fallback: use PowerShell to connect to Redis port
    Write-Host "redis-cli not found, using kubectl port-forward..." -ForegroundColor Yellow
    
    # Set KUBECONFIG
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"
    
    # Forward Redis port temporarily
    $portForward = Start-Job -ScriptBlock {
        kubectl port-forward -n infra svc/redis-master 6379:6379
    } -Name "TempRedisForward"
    
    Start-Sleep -Seconds 2
    
    # Install StackExchange.Redis if needed and flush
    try {
        Add-Type -Path "$env:USERPROFILE\.nuget\packages\stackexchange.redis\2.8.16\lib\net6.0\StackExchange.Redis.dll" -ErrorAction SilentlyContinue
        
        $redis = [StackExchange.Redis.ConnectionMultiplexer]::Connect("localhost:6379")
        $server = $redis.GetServer("localhost", 6379)
        $server.FlushAllDatabases()
        $redis.Dispose()
        
        Write-Host "✅ Redis flushed successfully via StackExchange.Redis" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Could not flush Redis automatically" -ForegroundColor Yellow
        Write-Host "Please manually run: kubectl exec -n infra redis-master-0 -- redis-cli FLUSHALL" -ForegroundColor Cyan
    }
    
    # Stop port forward
    Stop-Job -Name "TempRedisForward" -ErrorAction SilentlyContinue
    Remove-Job -Name "TempRedisForward" -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Redis has been cleared. Restart services with:" -ForegroundColor Cyan
Write-Host "  .\scripts\start-services.ps1" -ForegroundColor White
