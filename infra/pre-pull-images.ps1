# Pre-pull images to avoid ImagePullBackOff on first run
$ErrorActionPreference = "Continue"

Write-Host "`nPre-pulling required images..." -ForegroundColor Cyan
Write-Host "This reduces first-time startup delays`n" -ForegroundColor DarkGray

# Ensure Docker is available
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "Docker CLI not found in PATH. Skipping pre-pull." -ForegroundColor Yellow
    return
}

$images = @(
    # Kafka (Bitnami) + embedded ZooKeeper for local demo
    # "docker.io/bitnami/kafka:latest",
    # "docker.io/bitnami/zookeeper:latest",
    # Datastores & telemetry - use images matching Helm chart defaults where possible
    # Use Docker Hub mirror for Bitnami images to avoid registry.bitnami.com DNS issues
    "docker.io/bitnami/mongodb:latest",
    # Note: redis chart may use bitnami/redis by default; 'redis/redis-stack-server' is a different image (Redis Stack)
    "docker.io/redis/redis-stack-server:latest"
    # "otel/opentelemetry-collector-contrib:0.104.0"
)

$pulled = 0; $failed = 0
foreach ($image in $images) {
    Write-Host "  Pulling $image..." -NoNewline
    # Run docker pull and capture both output and exit code. External
    # executables do not throw PowerShell exceptions, so check $LASTEXITCODE.
    $output = & docker pull $image 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
        $pulled++
    } else {
        Write-Host " FAILED" -ForegroundColor Red
        $err = ($output -join "`n")
        Write-Host "    Error: $err" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
if ($failed -eq 0) { Write-Host "Successfully pre-pulled all $pulled images!" -ForegroundColor Green }
else {
    Write-Host "Pre-pulled $pulled images, $failed failed" -ForegroundColor Yellow
    Write-Host "Kubernetes will attempt to pull any missing images during deployment" -ForegroundColor Yellow
}
Write-Host ""
