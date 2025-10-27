<#
.SYNOPSIS
    Monitor all service jobs in real-time
.DESCRIPTION
    Continuously displays output from Gateway, Orchestrator, Builder, and Runner jobs
#>

$ErrorActionPreference = "Continue"

$jobs = Get-Job | Where-Object { $_.Name -in @('Gateway','Orchestrator','Builder','Runner') }

if ($jobs.Count -eq 0) {
    Write-Host "❌ No service jobs found. Start them first:" -ForegroundColor Red
    Write-Host "   .\scripts\start-monitored.ps1" -ForegroundColor Yellow
    exit 1
}

Write-Host "📊 Monitoring Service Jobs (Ctrl+C to exit)..." -ForegroundColor Cyan
Write-Host ""

$lastPositions = @{}
foreach ($job in $jobs) {
    $lastPositions[$job.Name] = 0
}

$colors = @{
    "Gateway" = "Magenta"
    "Orchestrator" = "Blue"
    "Builder" = "Cyan"
    "Runner" = "Green"
}

try {
    while ($true) {
        foreach ($job in $jobs) {
            $output = Receive-Job -Job $job -Keep 2>&1
            
            if ($output -and $output.Count -gt $lastPositions[$job.Name]) {
                $newLines = $output[$lastPositions[$job.Name]..($output.Count - 1)]
                $lastPositions[$job.Name] = $output.Count
                
                foreach ($line in $newLines) {
                    $lineStr = $line.ToString().Trim()
                    if ($lineStr -ne "") {
                        $timestamp = Get-Date -Format "HH:mm:ss.fff"
                        $serviceColor = $colors[$job.Name]
                        
                        # Determine line color
                        $lineColor = "Gray"
                        if ($lineStr -match "fail:|error|exception") { $lineColor = "Red" }
                        elseif ($lineStr -match "warn:") { $lineColor = "Yellow" }
                        elseif ($lineStr -match "✅|🧹|started|ready|SUCCESS") { $lineColor = "Green" }
                        
                        Write-Host "[$timestamp] " -NoNewline -ForegroundColor DarkGray
                        Write-Host "$($job.Name.PadRight(12))" -NoNewline -ForegroundColor $serviceColor
                        Write-Host " | " -NoNewline -ForegroundColor DarkGray
                        Write-Host $lineStr -ForegroundColor $lineColor
                    }
                }
            }
        }
        
        # Check if any jobs have failed
        $failedJobs = $jobs | Where-Object { $_.State -eq "Failed" }
        if ($failedJobs) {
            Write-Host ""
            Write-Host "❌ JOBS FAILED:" -ForegroundColor Red
            foreach ($fj in $failedJobs) {
                Write-Host "   $($fj.Name) - $($fj.ChildJobs[0].JobStateInfo.Reason.Message)" -ForegroundColor Red
            }
            break
        }
        
        Start-Sleep -Milliseconds 100
    }
}
catch {
    Write-Host ""
    Write-Host "Monitoring stopped" -ForegroundColor Yellow
}
