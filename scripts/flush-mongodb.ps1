# Flush MongoDB to clear all collections and data
Write-Host "Flushing MongoDB to clear all data..." -ForegroundColor Yellow

# Set KUBECONFIG
$repoRoot = Split-Path $PSScriptRoot -Parent
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

# Execute MongoDB flush command
try {
    Write-Host "Dropping reporunner database..." -ForegroundColor Cyan
    kubectl exec -n infra mongodb-0 -- mongosh --eval "use reporunner; db.dropDatabase();"
    Write-Host "✅ MongoDB database 'reporunner' dropped successfully" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Could not flush MongoDB automatically" -ForegroundColor Yellow
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Please manually run: kubectl exec -n infra mongodb-0 -- mongosh --eval 'use reporunner; db.dropDatabase();'" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "MongoDB has been cleared. Restart services if they are running." -ForegroundColor Cyan
