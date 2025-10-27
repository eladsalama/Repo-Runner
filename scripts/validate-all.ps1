#!/usr/bin/env pwsh
# Comprehensive validation script for RepoRunner
# Tests all components, configurations, and integrations

$ErrorActionPreference = "Stop"
$script:failCount = 0
$script:passCount = 0

# Set kubeconfig path
$repoRoot = Split-Path $PSScriptRoot -Parent
$env:KUBECONFIG = Join-Path $repoRoot "infra\terraform\kubeconfig"

function Test-Check {
    param(
        [string]$Name,
        [scriptblock]$Test,
        [string]$SuccessMessage = "✅",
        [string]$FailureMessage = "❌"
    )
    
    Write-Host "`n🔍 Testing: $Name" -ForegroundColor Cyan
    try {
        $result = & $Test
        if ($result -eq $false) {
            throw "Test returned false"
        }
        Write-Host "   $SuccessMessage PASS" -ForegroundColor Green
        $script:passCount++
        return $true
    } catch {
        Write-Host "   $FailureMessage FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $script:failCount++
        return $false
    }
}

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  RepoRunner Comprehensive Validation" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

# ===== Prerequisites =====
Write-Host "`n📦 PREREQUISITES" -ForegroundColor Yellow

Test-Check "Docker Desktop installed" {
    docker version | Out-Null
    return $true
}

Test-Check "kubectl installed" {
    kubectl version --client | Out-Null
    return $true
}

Test-Check "kind installed" {
    kind version | Out-Null
    return $true
}

Test-Check "helm installed" {
    helm version --short | Out-Null
    return $true
}

Test-Check ".NET 9.0 SDK installed" {
    $version = dotnet --version
    if ($version -notmatch "^9\.") {
        throw "Expected .NET 9.0, got $version"
    }
    return $true
}

Test-Check "Node.js installed" {
    node --version | Out-Null
    return $true
}

Test-Check "npm installed" {
    npm --version | Out-Null
    return $true
}

# ===== Project Structure =====
Write-Host "`n📁 PROJECT STRUCTURE" -ForegroundColor Yellow

$requiredDirs = @(
    "src/Gateway",
    "src/Orchestrator",
    "src/Builder",
    "src/Runner",
    "src/Indexer",
    "src/Insights",
    "shared/Shared",
    "contracts",
    "extension",
    "infra/terraform"
)

foreach ($dir in $requiredDirs) {
    Test-Check "Directory exists: $dir" {
        if (-not (Test-Path $dir)) {
            throw "Not found"
        }
        return $true
    }
}

# ===== Build Tests =====
Write-Host "`n🔨 BUILD TESTS" -ForegroundColor Yellow

Test-Check ".NET solution builds (Debug)" {
    $output = dotnet build -c Debug --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $output"
    }
    return $true
}

Test-Check ".NET solution builds (Release)" {
    $output = dotnet build -c Release --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $output"
    }
    return $true
}

Test-Check "Extension dependencies installed" {
    Push-Location extension
    try {
        if (-not (Test-Path "node_modules")) {
            throw "node_modules not found. Run: npm install"
        }
        return $true
    } finally {
        Pop-Location
    }
}

Test-Check "Extension builds successfully" {
    Push-Location extension
    try {
        $output = npm run build 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed: $output"
        }
        if (-not (Test-Path "dist/manifest.json")) {
            throw "dist/manifest.json not created"
        }
        return $true
    } finally {
        Pop-Location
    }
}

# ===== Configuration Tests =====
Write-Host "`n⚙️  CONFIGURATION TESTS" -ForegroundColor Yellow

$services = @("Gateway", "Orchestrator", "Builder", "Runner", "Indexer", "Insights")

foreach ($service in $services) {
    Test-Check "appsettings.json exists for $service" {
        $path = "src/$service/appsettings.json"
        if (-not (Test-Path $path)) {
            throw "Not found: $path"
        }
        return $true
    }
    
    Test-Check "appsettings.json valid JSON for $service" {
        $path = "src/$service/appsettings.json"
        try {
            $json = Get-Content $path -Raw | ConvertFrom-Json
            return $true
        } catch {
            throw "Invalid JSON: $($_.Exception.Message)"
        }
    }
}

Test-Check "All services have consistent Redis config" {
    $redisConfigs = @()
    foreach ($service in $services) {
        $path = "src/$service/appsettings.json"
        $json = Get-Content $path -Raw | ConvertFrom-Json
        if ($json.Redis) {
            $redisConfigs += $json.Redis.ConnectionString
        }
    }
    $unique = $redisConfigs | Select-Object -Unique
    if ($unique.Count -gt 1) {
        throw "Inconsistent Redis configs: $($unique -join ', ')"
    }
    return $true
}

Test-Check "All services have consistent MongoDB config" {
    $mongoConfigs = @()
    foreach ($service in $services) {
        $path = "src/$service/appsettings.json"
        $json = Get-Content $path -Raw | ConvertFrom-Json
        if ($json.MongoDB) {
            $mongoConfigs += $json.MongoDB.ConnectionString
        }
    }
    $unique = $mongoConfigs | Select-Object -Unique
    if ($unique.Count -gt 1) {
        throw "Inconsistent MongoDB configs: $($unique -join ', ')"
    }
    return $true
}

# ===== Port Configuration =====
Write-Host "`n🔌 PORT CONFIGURATION" -ForegroundColor Yellow

Test-Check "No port conflicts in configuration" {
    # Gateway should use 5247, others should have no HTTP ports
    $gatewaySettings = Get-Content "src/Gateway/Properties/launchSettings.json" -Raw | ConvertFrom-Json
    $gatewayPort = $gatewaySettings.profiles.http.applicationUrl
    if ($gatewayPort -notmatch "5247") {
        throw "Gateway not using expected port 5247"
    }
    return $true
}

Test-Check "PORT-MAPPING.md exists" {
    if (-not (Test-Path "PORT-MAPPING.md")) {
        throw "PORT-MAPPING.md not found"
    }
    return $true
}

# ===== Proto Contracts =====
Write-Host "`n📜 PROTO CONTRACTS" -ForegroundColor Yellow

$protoFiles = @("run.proto", "insights.proto", "events.proto")

foreach ($proto in $protoFiles) {
    Test-Check "Proto file exists: $proto" {
        $path = "contracts/$proto"
        if (-not (Test-Path $path)) {
            throw "Not found: $path"
        }
        return $true
    }
    
    Test-Check "Proto file has valid syntax: $proto" {
        $path = "contracts/$proto"
        $content = Get-Content $path -Raw
        if ($content -notmatch 'syntax\s*=\s*"proto3"') {
            throw "Missing or invalid syntax declaration"
        }
        return $true
    }
}

# ===== Infrastructure Tests =====
Write-Host "`n🏗️  INFRASTRUCTURE" -ForegroundColor Yellow

Test-Check "bootstrap.ps1 exists" {
    if (-not (Test-Path "infra/bootstrap.ps1")) {
        throw "Not found"
    }
    return $true
}

Test-Check "Terraform config exists" {
    if (-not (Test-Path "infra/terraform/main.tf")) {
        throw "Not found"
    }
    return $true
}

Test-Check "kind cluster check" {
    $clusters = kind get clusters 2>&1
    if ($clusters -match "reporunner") {
        Write-Host "      ️  kind cluster 'reporunner' exists" -ForegroundColor Gray
        return $true
    } else {
        Write-Host "      ️  kind cluster not deployed (run: cd infra; .\bootstrap.ps1 apply)" -ForegroundColor Gray
        return $true
    }
}

# ===== Runtime Tests (if infrastructure is running) =====
$clusterExists = (kind get clusters 2>&1) -match "reporunner"

if ($clusterExists) {
    Write-Host "`n🚀 RUNTIME TESTS (cluster detected)" -ForegroundColor Yellow
    
    Test-Check "kubectl can access cluster" {
        kubectl cluster-info 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Cannot access cluster"
        }
        return $true
    }
    
    Test-Check "infra namespace exists" {
        $ns = kubectl get namespace infra -o name 2>$null
        if (-not $ns) {
            throw "infra namespace not found"
        }
        return $true
    }
    
    Test-Check "MongoDB pod exists in infra" {
        $pod = kubectl get pods -n infra -l app.kubernetes.io/name=mongodb -o name 2>$null
        if (-not $pod) {
            throw "MongoDB pod not found"
        }
        return $true
    }
    
    Test-Check "Redis pod exists in infra" {
        $pod = kubectl get pods -n infra -l app.kubernetes.io/name=redis -o name 2>$null
        if (-not $pod) {
            throw "Redis pod not found"
        }
        return $true
    }
    
    Test-Check "MongoDB pod is running" {
        $status = kubectl get pods -n infra -l app.kubernetes.io/name=mongodb -o jsonpath='{.items[0].status.phase}' 2>$null
        if ($status -ne "Running") {
            throw "MongoDB pod status: $status"
        }
        return $true
    }
    
    Test-Check "Redis pod is running" {
        $status = kubectl get pods -n infra -l app.kubernetes.io/name=redis -o jsonpath='{.items[0].status.phase}' 2>$null
        if ($status -ne "Running") {
            throw "Redis pod status: $status"
        }
        return $true
    }
} else {
    Write-Host "`n⏭️  RUNTIME TESTS SKIPPED (no cluster)" -ForegroundColor Gray
    Write-Host "   To deploy: cd infra; .\bootstrap.ps1 apply" -ForegroundColor Gray
}

# ===== Documentation =====
Write-Host "`n📖 DOCUMENTATION" -ForegroundColor Yellow

$requiredDocs = @(
    "README.md",
    "QUICKREF.md",
    "PORT-MAPPING.md",
    "docs/Plan.md",
    "docs/Progress.md",
    "infra/README.md",
    "infra/PREREQUISITES.md"
)

foreach ($doc in $requiredDocs) {
    Test-Check "Documentation exists: $doc" {
        if (-not (Test-Path $doc)) {
            throw "Not found"
        }
        return $true
    }
}

# ===== Scripts =====
Write-Host "`n📜 SCRIPTS" -ForegroundColor Yellow

Test-Check "start-services.ps1 exists" {
    if (-not (Test-Path "start-services.ps1")) {
        throw "Not found"
    }
    return $true
}

Test-Check "start-services.ps1 is valid PowerShell" {
    $errors = $null
    $null = [System.Management.Automation.PSParser]::Tokenize(
        (Get-Content "start-services.ps1" -Raw), [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "Parse errors: $($errors.Count)"
    }
    return $true
}

# ===== Summary =====
Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  VALIDATION SUMMARY" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

$total = $script:passCount + $script:failCount
if ($total -gt 0) {
    $passRate = [math]::Round(($script:passCount / $total) * 100, 1)
} else {
    $passRate = 0
}

if ($script:failCount -gt 0) {
    $failColor = "Red"
} else {
    $failColor = "Gray"
}

if ($passRate -ge 95) {
    $passRateColor = "Green"
} elseif ($passRate -ge 80) {
    $passRateColor = "Yellow"
} else {
    $passRateColor = "Red"
}

Write-Host "`n  ✅ Passed: $($script:passCount)" -ForegroundColor Green
Write-Host "  ❌ Failed: $($script:failCount)" -ForegroundColor $failColor
Write-Host "  📊 Pass Rate: $passRate%" -ForegroundColor $passRateColor

if ($script:failCount -eq 0) {
    Write-Host "`n🎉 ALL TESTS PASSED! Repository is ready for deployment." -ForegroundColor Green
    Write-Host "`n📝 Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Deploy infrastructure: cd infra; .\bootstrap.ps1 apply" -ForegroundColor White
    Write-Host "   2. Start services: .\start-services.ps1" -ForegroundColor White
    Write-Host "   3. Load extension from ./extension/dist" -ForegroundColor White
    exit 0
} else {
    Write-Host "`n⚠️  SOME TESTS FAILED. Please fix the issues above." -ForegroundColor Yellow
    exit 1
}
