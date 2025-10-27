<#
.SYNOPSIS
    Automatically detect errors and issues in service logs
.DESCRIPTION
    Scans service logs for errors, exceptions, and issues.
    Provides summary and detailed error analysis.
#>

param(
    [int]$Last = 50,              # Number of recent lines to scan per service
    [switch]$Detailed = $false,   # Show full stack traces
    [switch]$Watch = $false       # Continuously monitor for new errors
)

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$logsDir = Join-Path $repoRoot "logs"

function Get-ErrorSummary {
    param(
        [string]$service,
        [string]$logPath
    )
    
    if (-not (Test-Path $logPath)) {
        return @{
            Service = $service
            HasErrors = $false
            Message = "Log file not found"
        }
    }
    
    # Read last N lines
    $lines = Get-Content $logPath -Tail $Last -ErrorAction SilentlyContinue
    
    if (-not $lines) {
        return @{
            Service = $service
            HasErrors = $false
            Message = "No logs yet"
        }
    }
    
    # Detect common issues
    $errors = @()
    $warnings = @()
    $protobufErrors = @()
    $raceConditions = @()
    $exceptions = @()
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        
        # Protobuf errors
        if ($line -match "InvalidProtocolBufferException|String is invalid UTF-8|input ended unexpectedly") {
            $context = $lines[([Math]::Max(0, $i-2))..([Math]::Min($lines.Count-1, $i+5))] -join "`n"
            $protobufErrors += @{Line=$i; Context=$context}
        }
        
        # Race conditions
        if ($line -match "Run not found.*race condition") {
            $raceConditions += @{Line=$i; Text=$line}
        }
        
        # General errors
        if ($line -match "\[ERROR\]|fail:|Error processing") {
            $errors += @{Line=$i; Text=$line}
        }
        
        # Warnings
        if ($line -match "\[WARNING\]|warn:") {
            $warnings += @{Line=$i; Text=$line}
        }
        
        # Exceptions
        if ($line -match "Exception:") {
            $context = $lines[([Math]::Max(0, $i))..([Math]::Min($lines.Count-1, $i+10))] -join "`n"
            $exceptions += @{Line=$i; Context=$context}
        }
    }
    
    return @{
        Service = $service
        HasErrors = ($errors.Count -gt 0 -or $exceptions.Count -gt 0)
        HasWarnings = ($warnings.Count -gt 0)
        ErrorCount = $errors.Count
        WarningCount = $warnings.Count
        ProtobufErrors = $protobufErrors
        RaceConditions = $raceConditions
        Errors = $errors
        Exceptions = $exceptions
        TotalLines = $lines.Count
    }
}

function Show-ErrorReport {
    param($summary)
    
    $serviceColor = switch ($summary.Service) {
        "Gateway" { "Magenta" }
        "Orchestrator" { "Blue" }
        "Builder" { "Cyan" }
        "Runner" { "Green" }
        default { "White" }
    }
    
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host " $($summary.Service)" -ForegroundColor $serviceColor
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    
    if (-not $summary.HasErrors -and -not $summary.HasWarnings) {
        Write-Host "  ✅ No issues detected" -ForegroundColor Green
        return
    }
    
    # Protobuf errors (critical)
    if ($summary.ProtobufErrors.Count -gt 0) {
        Write-Host ""
        Write-Host "  🔴 CRITICAL: Protobuf Deserialization Errors ($($summary.ProtobufErrors.Count))" -ForegroundColor Red
        Write-Host "      → Corrupted Redis messages detected" -ForegroundColor Yellow
        Write-Host "      → Fix: Restart Gateway (it will auto-flush on startup)" -ForegroundColor Yellow
        
        if ($Detailed) {
            foreach ($err in $summary.ProtobufErrors[0..2]) {  # Show first 3
                Write-Host ""
                Write-Host "      Context:" -ForegroundColor DarkGray
                foreach ($line in ($err.Context -split "`n")) {
                    Write-Host "        $line" -ForegroundColor DarkRed
                }
            }
        }
    }
    
    # Race conditions (expected behavior)
    if ($summary.RaceConditions.Count -gt 0) {
        Write-Host ""
        Write-Host "  ⚠️  Race Condition Retries ($($summary.RaceConditions.Count))" -ForegroundColor Yellow
        Write-Host "      → Expected behavior: Events arrive before Run record created" -ForegroundColor Gray
        Write-Host "      → System will auto-retry. No action needed." -ForegroundColor Gray
    }
    
    # General errors
    if ($summary.ErrorCount -gt 0) {
        Write-Host ""
        Write-Host "  ❌ Errors ($($summary.ErrorCount))" -ForegroundColor Red
        
        foreach ($err in $summary.Errors[0..4]) {  # Show first 5
            Write-Host "      • $($err.Text)" -ForegroundColor Red
        }
    }
    
    # Exceptions
    if ($summary.Exceptions.Count -gt 0) {
        Write-Host ""
        Write-Host "  💥 Exceptions ($($summary.Exceptions.Count))" -ForegroundColor Red
        
        if ($Detailed) {
            foreach ($ex in $summary.Exceptions[0..1]) {  # Show first 2
                Write-Host ""
                Write-Host "      Stack trace:" -ForegroundColor DarkGray
                foreach ($line in ($ex.Context -split "`n")[0..15]) {  # First 15 lines
                    Write-Host "        $line" -ForegroundColor DarkRed
                }
            }
        } else {
            Write-Host "      Run with -Detailed flag to see stack traces" -ForegroundColor Gray
        }
    }
    
    # Warnings
    if ($summary.WarningCount -gt 0) {
        Write-Host ""
        Write-Host "  ⚠️  Warnings ($($summary.WarningCount))" -ForegroundColor Yellow
    }
}

# Main execution
Write-Host "🔍 Scanning service logs for errors..." -ForegroundColor Cyan
Write-Host "   Analyzing last $Last lines per service" -ForegroundColor Gray
Write-Host ""

$services = @("Gateway", "Orchestrator", "Builder", "Runner")

do {
    $allSummaries = @()
    $hasAnyCritical = $false
    
    foreach ($svc in $services) {
        $logPath = Join-Path $logsDir "$($svc.ToLower()).log"
        $summary = Get-ErrorSummary -service $svc -logPath $logPath
        $allSummaries += $summary
        
        if ($summary.ProtobufErrors.Count -gt 0) {
            $hasAnyCritical = $true
        }
    }
    
    # Clear screen if watching
    if ($Watch) {
        Clear-Host
        Write-Host "🔍 Error Monitor (refreshing every 5s, Ctrl+C to exit)" -ForegroundColor Cyan
        Write-Host "   Last updated: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Gray
    }
    
    # Show reports
    foreach ($summary in $allSummaries) {
        Show-ErrorReport $summary
    }
    
    # Overall status
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host " OVERALL STATUS" -ForegroundColor White
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    
    $totalErrors = ($allSummaries | Measure-Object -Property ErrorCount -Sum).Sum
    $totalWarnings = ($allSummaries | Measure-Object -Property WarningCount -Sum).Sum
    $totalProtobuf = ($allSummaries | ForEach-Object { $_.ProtobufErrors.Count } | Measure-Object -Sum).Sum
    
    if ($hasAnyCritical) {
        Write-Host "  🔴 CRITICAL ISSUES DETECTED" -ForegroundColor Red
        Write-Host "     Protobuf errors: $totalProtobuf" -ForegroundColor Red
        Write-Host ""
        Write-Host "  🔧 RECOMMENDED FIX:" -ForegroundColor Yellow
        Write-Host "     1. Stop all services (Ctrl+C in each terminal)" -ForegroundColor White
        Write-Host "     2. Restart: .\scripts\start-services.ps1" -ForegroundColor White
        Write-Host "     3. Gateway will auto-flush Redis on startup" -ForegroundColor White
    }
    elseif ($totalErrors -gt 0) {
        Write-Host "  ⚠️  Issues found: $totalErrors errors, $totalWarnings warnings" -ForegroundColor Yellow
    }
    else {
        Write-Host "  ✅ All services healthy" -ForegroundColor Green
    }
    
    Write-Host ""
    
    if ($Watch) {
        Start-Sleep -Seconds 5
    }
    
} while ($Watch)
