# Pre-pull all required images to avoid ImagePullBackOff errors
# This ensures Docker has images cached before Kubernetes tries to use them

$ErrorActionPreference = "Continue"

Write-Host "`nPre-pulling required images..." -ForegroundColor Cyan
Write-Host "This ensures no ImagePullBackOff errors during deployment`n" -ForegroundColor DarkGray

# Use major version tags which Bitnami supports (not 'latest')
$images = @(
    "bitnami/kafka:3.9",
    "bitnami/mongodb:8.0", 
    "redis/redis-stack-server:latest",
    "otel/opentelemetry-collector-contrib:latest"
)

$pulled = 0
$failed = 0

foreach ($image in $images) {
    Write-Host "  Pulling $image..." -NoNewline
    $result = docker pull $image 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
        $pulled++
    } else {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "    Error: $result" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
if ($failed -eq 0) {
    Write-Host "Successfully pre-pulled all $pulled images!" -ForegroundColor Green
} else {
    Write-Host "Pre-pulled $pulled images, $failed failed" -ForegroundColor Yellow
    Write-Host "The failed images will be pulled by Kubernetes (may take longer)" -ForegroundColor Yellow
}
Write-Host ""
