# Quick script to set KUBECONFIG for the current PowerShell session
# Usage: . .\infra\set-kubeconfig.ps1  (note the dot at the beginning!)

$KubeconfigPath = Join-Path $PSScriptRoot "terraform\kubeconfig"

if (Test-Path $KubeconfigPath) {
    $env:KUBECONFIG = $KubeconfigPath
    Write-Host "KUBECONFIG set to: $KubeconfigPath" -ForegroundColor Green
    Write-Host "You can now use kubectl commands." -ForegroundColor Cyan
} else {
    Write-Host "ERROR: kubeconfig not found at $KubeconfigPath" -ForegroundColor Red
    Write-Host "Run: .\infra\bootstrap.ps1 apply" -ForegroundColor Yellow
}
