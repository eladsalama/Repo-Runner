# Flush both Redis and MongoDB to clear all data
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "Flushing Redis and MongoDB" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

$repoRoot = Split-Path $PSScriptRoot -Parent

# Flush Redis
Write-Host "1. Flushing Redis..." -ForegroundColor Yellow
& "$PSScriptRoot\flush-redis.ps1"

Write-Host ""
Write-Host "-------------------------------------------" -ForegroundColor DarkGray
Write-Host ""

# Flush MongoDB
Write-Host "2. Flushing MongoDB..." -ForegroundColor Yellow
& "$PSScriptRoot\flush-mongodb.ps1"

Write-Host ""
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "✅ All data cleared!" -ForegroundColor Green
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now restart the services with:" -ForegroundColor Cyan
Write-Host "  .\scripts\start-monitored.ps1" -ForegroundColor White
Write-Host ""
