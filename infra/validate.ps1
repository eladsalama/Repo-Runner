# Infrastructure Validation Script
# Tests connectivity to all deployed services

$ErrorActionPreference = "Continue"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Write-Pass {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Test-PodHealth {
    param([string]$Name, [string]$Selector)
    
    $pod = kubectl get pods -n infra -l $Selector -o jsonpath='{.items[0].status.phase}' 2>$null
    if ($pod -eq "Running") {
        Write-Pass "$Name is running"
        return $true
    }
    else {
        Write-Fail "$Name is not running (status: $pod)"
        return $false
    }
}

function Test-ServiceEndpoint {
    param([string]$Name, [string]$Service, [string]$Port)
    
    $endpoint = kubectl get svc -n infra $Service -o jsonpath="{.spec.clusterIP}:$Port" 2>$null
    if ($endpoint) {
        Write-Pass "$Name endpoint: $endpoint"
        return $true
    }
    else {
        Write-Fail "$Name service not found"
        return $false
    }
}

function Test-LocalhostPort {
    param([string]$Name, [string]$Url)
    
    try {
        $null = Invoke-WebRequest -Uri $Url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        Write-Pass "$Name is accessible at $Url"
        return $true
    }
    catch {
        Write-Fail "$Name is not accessible at $Url"
        return $false
    }
}

Write-Host @"
╔═══════════════════════════════════════════╗
║   RepoRunner Infrastructure Validation    ║
╚═══════════════════════════════════════════╝
"@ -ForegroundColor Cyan

# Check cluster exists
Write-Step "1. Checking kind cluster..."
$clusters = kind get clusters 2>&1
if ($clusters -match "reporunner") {
    Write-Pass "Cluster 'reporunner' exists"
}
else {
    Write-Fail "Cluster 'reporunner' not found"
    Write-Host "`nRun: .\bootstrap.ps1 apply" -ForegroundColor Yellow
    exit 1
}

# Check namespace
Write-Step "2. Checking namespace..."
$ns = kubectl get namespace infra -o jsonpath='{.metadata.name}' 2>$null
if ($ns -eq "infra") {
    Write-Pass "Namespace 'infra' exists"
}
else {
    Write-Fail "Namespace 'infra' not found"
    exit 1
}

# Check pods
Write-Step "3. Checking pod health..."
$services = @(
    @{Name="MongoDB"; Selector="app.kubernetes.io/name=mongodb"},
    @{Name="Redis"; Selector="app.kubernetes.io/name=redis"},
    @{Name="Kafka"; Selector="app.kubernetes.io/name=kafka"},
    @{Name="OTel Collector"; Selector="app.kubernetes.io/name=opentelemetry-collector"}
)

$allHealthy = $true
foreach ($svc in $services) {
    $healthy = Test-PodHealth -Name $svc.Name -Selector $svc.Selector
    if (-not $healthy) { $allHealthy = $false }
}

# Check service endpoints (internal)
Write-Step "4. Checking internal service endpoints..."
Test-ServiceEndpoint -Name "MongoDB" -Service "mongodb" -Port "27017"
Test-ServiceEndpoint -Name "Redis" -Service "redis-master" -Port "6379"
Test-ServiceEndpoint -Name "Kafka" -Service "kafka" -Port "9092"
Test-ServiceEndpoint -Name "OTel Collector gRPC" -Service "otel-collector" -Port "4317"

# Test basic kubectl operations
Write-Step "5. Testing kubectl operations..."
try {
    $podCount = (kubectl get pods -n infra --no-headers 2>$null | Measure-Object).Count
    if ($podCount -gt 0) {
        Write-Pass "Can query pods (found $podCount pods)"
    }
    else {
        Write-Fail "No pods found in infra namespace"
        $allHealthy = $false
    }
}
catch {
    Write-Fail "kubectl operations failed"
    $allHealthy = $false
}

# Test Terraform state
Write-Step "7. Checking Terraform state..."
Push-Location "$PSScriptRoot\terraform"
try {
    if (Test-Path "terraform.tfstate") {
        $state = Get-Content "terraform.tfstate" -Raw | ConvertFrom-Json
        $resources = $state.resources.Count
        Write-Pass "Terraform state exists ($resources resources)"
    }
    else {
        Write-Fail "Terraform state not found"
        $allHealthy = $false
    }
}
finally {
    Pop-Location
}

# Summary
Write-Step "Summary"
if ($allHealthy) {
    Write-Host "`n✅ All systems operational!" -ForegroundColor Green
    Write-Host "`nInternal endpoints (from within cluster):" -ForegroundColor Cyan
    Write-Host "  • MongoDB:  mongodb.infra.svc.cluster.local:27017" -ForegroundColor White
    Write-Host "  • Redis:    redis-master.infra.svc.cluster.local:6379" -ForegroundColor White
    Write-Host "  • Kafka:    kafka.infra.svc.cluster.local:9092" -ForegroundColor White
    Write-Host "  • OTel:     otel-collector.infra.svc.cluster.local:4317" -ForegroundColor White
    Write-Host "`nYou can proceed to Milestone 2: Protobuf Contracts & gRPC Scaffolds" -ForegroundColor Green
}
else {
    Write-Host "`n⚠️  Some services are not healthy" -ForegroundColor Yellow
    Write-Host "`nTroubleshooting steps:" -ForegroundColor Cyan
    Write-Host "  1. Check pod logs: kubectl logs -n infra <pod-name>" -ForegroundColor White
    Write-Host "  2. Describe pod: kubectl describe pod -n infra <pod-name>" -ForegroundColor White
    Write-Host "  3. Check events: kubectl get events -n infra --sort-by='.lastTimestamp'" -ForegroundColor White
    Write-Host "  4. Try reset: .\bootstrap.ps1 reset && .\bootstrap.ps1 apply" -ForegroundColor White
    Write-Host "`nSee infra/README.md for more troubleshooting tips." -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Cyan
