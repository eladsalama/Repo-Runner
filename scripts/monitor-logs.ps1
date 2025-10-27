<#
.SYNOPSIS
    Real-time log monitoring for all RepoRunner services
.DESCRIPTION
    Tails logs from Gateway, Orchestrator, Builder, and Runner with color coding.
    Can filter by service, log level, or search terms.
#>

param(
    [string]$Service = "all",        # Gateway, Orchestrator, Builder, Runner, or "all"
    [string]$Level = "all",          # info, warn, error, fail, or "all"
    [string]$Search = "",            # Search term to filter logs
    [switch]$FollowJobs = $true      # Monitor running jobs by default (services output to console)
)

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$logsDir = Join-Path $repoRoot "logs"

# Color mapping for log levels
$colors = @{
    "info" = "White"
    "warn" = "Yellow"
    "error" = "Red"
    "fail" = "Red"
    "success" = "Green"
}

function Get-LogColor {
    param([string]$line)
    
    if ($line -match "\[ERROR\]|fail:|Error|Exception") { return "Red" }
    if ($line -match "\[WARNING\]|warn:") { return "Yellow" }
    if ($line -match "🧹|✅|SUCCESS|started|completed") { return "Green" }
    if ($line -match "⚠️|WARNING") { return "Yellow" }
    if ($line -match "❌|FAILED") { return "Red" }
    if ($line -match "info:") { return "Cyan" }
    
    return "Gray"
}

function Format-ServiceName {
    param([string]$name)
    
    $colors = @{
        "Gateway" = "Magenta"
        "Orchestrator" = "Blue"
        "Builder" = "Cyan"
        "Runner" = "Green"
    }
    
    return $colors[$name]
}

function Show-LogLine {
    param(
        [string]$service,
        [string]$line
    )
    
    # Filter by level if specified
    if ($Level -ne "all") {
        if ($line -notmatch $Level) { return }
    }
    
    # Filter by search term if specified
    if ($Search -ne "" -and $line -notmatch [regex]::Escape($Search)) {
        return
    }
    
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    $serviceColor = Format-ServiceName $service
    $lineColor = Get-LogColor $line
    
    Write-Host "[$timestamp] " -NoNewline -ForegroundColor DarkGray
    Write-Host "$service" -NoNewline -ForegroundColor $serviceColor
    Write-Host " | " -NoNewline -ForegroundColor DarkGray
    Write-Host $line -ForegroundColor $lineColor
}

# Main monitoring logic
if ($FollowJobs) {
    # Monitor background jobs (if services started with start-services.ps1)
    Write-Host "📊 Monitoring service jobs (Ctrl+C to exit)..." -ForegroundColor Cyan
    Write-Host ""
    
    $jobs = Get-Job | Where-Object { $_.Name -in @("Gateway","Orchestrator","Builder","Runner") }
    
    if ($jobs.Count -eq 0) {
        Write-Host "❌ No service jobs found. Start services first:" -ForegroundColor Red
        Write-Host "   .\scripts\start-services.ps1" -ForegroundColor Yellow
        exit 1
    }
    
    $lastLines = @{}
    foreach ($job in $jobs) {
        $lastLines[$job.Name] = 0
    }
    
    while ($true) {
        foreach ($job in $jobs) {
            $output = Receive-Job -Job $job -Keep
            
            if ($output) {
                $lines = $output -split "`n"
                $newLines = $lines[$lastLines[$job.Name]..($lines.Length-1)]
                $lastLines[$job.Name] = $lines.Length
                
                foreach ($line in $newLines) {
                    if ($line.Trim() -ne "") {
                        Show-LogLine -service $job.Name -line $line.Trim()
                    }
                }
            }
        }
        
        Start-Sleep -Milliseconds 100
    }
    
} else {
    # Monitor log files
    $logFiles = @()
    
    if ($Service -eq "all") {
        $logFiles = @(
            @{Service="Gateway"; Path=Join-Path $logsDir "gateway.log"}
            @{Service="Orchestrator"; Path=Join-Path $logsDir "orchestrator.log"}
            @{Service="Builder"; Path=Join-Path $logsDir "builder.log"}
            @{Service="Runner"; Path=Join-Path $logsDir "runner.log"}
        )
    } else {
        $logFiles = @(
            @{Service=$Service; Path=Join-Path $logsDir "$($Service.ToLower()).log"}
        )
    }
    
    # Check if log files exist
    $missingLogs = @()
    foreach ($log in $logFiles) {
        if (-not (Test-Path $log.Path)) {
            $missingLogs += $log.Service
        }
    }
    
    if ($missingLogs.Count -gt 0) {
        Write-Host "❌ Log files not found for: $($missingLogs -join ', ')" -ForegroundColor Red
        Write-Host ""
        Write-Host "Are services running? Check:" -ForegroundColor Yellow
        Write-Host "   Get-Job | Where-Object { `$_.Name -in @('Gateway','Orchestrator','Builder','Runner') }" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Or start services:" -ForegroundColor Yellow
        Write-Host "   .\scripts\start-services.ps1" -ForegroundColor Gray
        exit 1
    }
    
    Write-Host "📊 Monitoring logs from $logsDir" -ForegroundColor Cyan
    if ($Search -ne "") {
        Write-Host "🔍 Filtering by: $Search" -ForegroundColor Yellow
    }
    if ($Level -ne "all") {
        Write-Host "📌 Level filter: $Level" -ForegroundColor Yellow
    }
    Write-Host "Press Ctrl+C to exit" -ForegroundColor Gray
    Write-Host ""
    
    # Tail all log files
    $fileStreams = @{}
    $positions = @{}
    
    foreach ($log in $logFiles) {
        $fileStreams[$log.Service] = [System.IO.FileStream]::new($log.Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $positions[$log.Service] = $fileStreams[$log.Service].Length  # Start at end
    }
    
    try {
        while ($true) {
            $hasNew = $false
            
            foreach ($log in $logFiles) {
                $stream = $fileStreams[$log.Service]
                $stream.Seek($positions[$log.Service], [System.IO.SeekOrigin]::Begin) | Out-Null
                
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true, 4096, $true)
                
                while (-not $reader.EndOfStream) {
                    $line = $reader.ReadLine()
                    if ($line -and $line.Trim() -ne "") {
                        Show-LogLine -service $log.Service -line $line.Trim()
                        $hasNew = $true
                    }
                }
                
                $positions[$log.Service] = $stream.Position
            }
            
            if (-not $hasNew) {
                Start-Sleep -Milliseconds 100
            }
        }
    }
    finally {
        foreach ($stream in $fileStreams.Values) {
            $stream.Close()
        }
    }
}
