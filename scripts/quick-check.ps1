# Quick validation - run before demo
Write-Host "`nRepoRunner Quick Check`n" -ForegroundColor Cyan

$pass = 0
$fail = 0

# Get repo root directory
$repoRoot = Split-Path $PSScriptRoot -Parent

# Set kubeconfig path
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

Push-Location $repoRoot

Write-Host "1. Build..." -NoNewline
dotnet build -c Release --nologo -v quiet 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host " PASS" -ForegroundColor Green
    $pass++
} else {
    Write-Host " FAIL" -ForegroundColor Red
    $fail++
}

Write-Host "2. Extension..." -NoNewline
if (Test-Path "extension/dist/manifest.json") {
    Write-Host " PASS" -ForegroundColor Green
    $pass++
} else {
    Write-Host " FAIL" -ForegroundColor Red
    $fail++
}

Write-Host "3. Cluster..." -NoNewline
$cluster = kind get clusters 2>&1 | Select-String "reporunner"
if ($cluster) {
    Write-Host " PASS" -ForegroundColor Green
    $pass++
} else {
    Write-Host " SKIP" -ForegroundColor Yellow
}

if ($cluster) {
    Write-Host "4. Infrastructure..." -NoNewline
    $mongoPod = kubectl get pods -n infra -l app.kubernetes.io/name=mongodb -o jsonpath='{.items[0].status.phase}' 2>$null
    $redisPod = kubectl get pods -n infra -l app.kubernetes.io/name=redis -o jsonpath='{.items[0].status.phase}' 2>$null
    if ($mongoPod -eq "Running" -and $redisPod -eq "Running") {
        Write-Host " PASS" -ForegroundColor Green
        $pass++
    } else {
        Write-Host " FAIL" -ForegroundColor Red
        $fail++
    }
}

Write-Host "5. Configuration..." -NoNewline
$configs = @("Gateway", "Orchestrator", "Builder", "Runner") | ForEach-Object {
    $json = Get-Content "src/$_/appsettings.json" -Raw | ConvertFrom-Json
    return $json.Redis.ConnectionString
}
$unique = $configs | Select-Object -Unique
if ($unique.Count -eq 1) {
    Write-Host " PASS" -ForegroundColor Green
    $pass++
} else {
    Write-Host " FAIL" -ForegroundColor Red
    $fail++
}

Write-Host "`n================================" -ForegroundColor Cyan
if ($fail -eq 0) {
    Write-Host "READY FOR DEMO ($pass checks passed)" -ForegroundColor Green
    Write-Host "`nNext: .\scripts\start-services.ps1`n" -ForegroundColor White
    Pop-Location
    exit 0
} else {
    Write-Host "Found $fail issues`n" -ForegroundColor Red
    Pop-Location
    exit 1
}
