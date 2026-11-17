<#
.SYNOPSIS
    Monitor delle performance e della memoria per i processi AeroSpot (Host, Worker, Tools).
.DESCRIPTION
    Campiona memoria e CPU a intervalli regolari (predefinito: 2 secondi), registrando su console
    e su file CSV le variazioni di memoria (allocazioni, rilasci GC/EmptyWorkingSet), utilizzo CPU
    e cambi di stato dei processi.
.PARAMETER IntervalSeconds
    Intervallo di campionamento in secondi. Predefinito: 2.
.PARAMETER MinRamDeltaMb
    Variazione minima di RAM in MB per registrare un evento. Predefinito: 5.0 MB.
.PARAMETER MinRamPercentDelta
    Variazione percentuale minima di RAM per registrare un evento. Predefinito: 10% (0.10).
.PARAMETER MinCpuDelta
    Variazione percentuale minima di CPU per registrare un evento. Predefinito: 1.5%.
#>

param(
    [int]$IntervalSeconds = 2,
    [double]$MinRamDeltaMb = 5.0,
    [double]$MinRamPercentDelta = 0.10,
    [double]$MinCpuDelta = 1.5
)

$targetProcessNames = @("AeroSpot.Host", "AeroSpot.Worker", "AeroSpot.Tools", "AeroSpot")
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)

# Cartella di output log
$logDir = Join-Path $projectRoot "_temp\performance-logs"
if (-not (Test-Path $logDir)) {
    try {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    catch {
        $logDir = Join-Path $env:TEMP "AeroSpot_PerformanceLogs"
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
    }
}

$devLogDir = "E:\Antigravity\AeroSpot\Progetto\_temp\performance-logs"

$dateStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$csvFile = Join-Path $logDir "performance_$dateStamp.csv"

# Intestazione CSV
"Timestamp,ProcessName,PID,WorkingSetMB,PrivateMemoryMB,CPUPercent,Status,Trigger" | Out-File -FilePath $csvFile -Encoding utf8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AeroSpot - Performance & Memory Monitor                " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Intervallo campionamento: ${IntervalSeconds}s" -ForegroundColor Gray
Write-Host " Soglie registrazione:     RAM >= ${MinRamDeltaMb} MB (o >= $([int]($MinRamPercentDelta*100))%), CPU >= ${MinCpuDelta}%" -ForegroundColor Gray
Write-Host " File CSV di output:       $csvFile" -ForegroundColor Yellow
Write-Host " Premi CTRL+C in qualsiasi momento per arrestare." -ForegroundColor DarkGray
Write-Host "==========================================================" -ForegroundColor Cyan

$prevSampleTimes = @{}
$prevCpuTimes = @{}
$lastLoggedState = @{}
$processorCount = [Environment]::ProcessorCount

try {
    $firstRun = $true
    while ($true) {
        $now = Get-Date
        $timestamp = $now.ToString("yyyy-MM-dd HH:mm:ss")
        $foundAny = $false

        foreach ($pName in $targetProcessNames) {
            $processes = Get-Process -Name $pName -ErrorAction SilentlyContinue

            if ($processes) {
                $foundAny = $true
                foreach ($proc in $processes) {
                    $pidNum = $proc.Id
                    $wsMb = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
                    $privMb = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
                    
                    # Calcolo CPU % differenziale
                    $cpuPercent = 0.0
                    $procKey = "$pName`_$pidNum"

                    try {
                        $currCpu = $proc.TotalProcessorTime.TotalMilliseconds
                        if ($prevCpuTimes.ContainsKey($procKey) -and $prevSampleTimes.ContainsKey($procKey)) {
                            $timeDeltaMs = ($now - $prevSampleTimes[$procKey]).TotalMilliseconds
                            $cpuDeltaMs = $currCpu - $prevCpuTimes[$procKey]

                            if ($timeDeltaMs -gt 0) {
                                $cpuPercent = [Math]::Round(($cpuDeltaMs / ($timeDeltaMs * $processorCount)) * 100, 2)
                            }
                        }

                        $prevCpuTimes[$procKey] = $currCpu
                        $prevSampleTimes[$procKey] = $now
                    }
                    catch {
                        $cpuPercent = 0.0
                    }

                    # Verifica se la variazione è rilevante
                    $shouldLog = $false
                    $reason = ""
                    $color = "Green"

                    if (-not $lastLoggedState.ContainsKey($procKey)) {
                        $shouldLog = $true
                        $reason = "Baseline"
                        $color = "Cyan"
                    }
                    else {
                        $last = $lastLoggedState[$procKey]
                        $ramDiff = $wsMb - $last.WS
                        $absRamDiff = [Math]::Abs($ramDiff)
                        $ramRatio = if ($last.WS -gt 0) { $absRamDiff / $last.WS } else { 1.0 }
                        $cpuDiff = [Math]::Abs($cpuPercent - $last.CPU)

                        if ($absRamDiff -ge $MinRamDeltaMb -or $ramRatio -ge $MinRamPercentDelta) {
                            $shouldLog = $true
                            if ($ramDiff -gt 0) {
                                $reason = "RAM +$([Math]::Round($ramDiff, 1)) MB ($($last.WS) MB -> ${wsMb} MB)"
                                $color = "Yellow"
                            }
                            else {
                                $reason = "RAM $([Math]::Round($ramDiff, 1)) MB ($($last.WS) MB -> ${wsMb} MB)"
                                $color = "Green"
                            }
                        }
                        elseif ($cpuDiff -ge $MinCpuDelta -and ($cpuPercent -ge 1.0 -or $last.CPU -ge 1.0)) {
                            $shouldLog = $true
                            $reason = "CPU: $($last.CPU)% -> ${cpuPercent}%"
                            $color = "Magenta"
                        }
                    }

                    if ($shouldLog) {
                        $lastLoggedState[$procKey] = @{ WS = $wsMb; CPU = $cpuPercent; Status = "Running" }
                        $csvLine = "$timestamp,$pName,$pidNum,$wsMb,$privMb,$cpuPercent,Running,$reason"
                        Add-Content -Path $csvFile -Value $csvLine
                        Write-Host "[$timestamp] [$pName (PID $pidNum)] WS: ${wsMb} MB | Priv: ${privMb} MB | CPU: ${cpuPercent}% -> $reason" -ForegroundColor $color
                    }
                }
            }
            else {
                # Processo non in esecuzione
                $procKey = "$pName`_none"
                if (-not $lastLoggedState.ContainsKey($procKey) -or $lastLoggedState[$procKey].Status -ne "NotRunning") {
                    if ($lastLoggedState.ContainsKey($procKey)) {
                        Write-Host "[$timestamp] [STOP] $pName terminato/chiuso." -ForegroundColor Red
                    }
                    $lastLoggedState[$procKey] = @{ WS = 0; CPU = 0; Status = "NotRunning" }
                    $csvLine = "$timestamp,$pName,,0,0,0,NotRunning,Stopped"
                    Add-Content -Path $csvFile -Value $csvLine
                }
            }
        }

        if (-not $foundAny -and $firstRun) {
            Write-Host "[$timestamp] [ATTENZIONE] Nessun processo AeroSpot attivo al momento." -ForegroundColor Yellow
            Write-Host "             Avvia AeroSpot (AeroSpot.exe) per iniziare il campionamento." -ForegroundColor Gray
        }

        $firstRun = $false
        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host " Monitoraggio arrestato." -ForegroundColor Cyan
    Write-Host " Dati salvati in: $csvFile" -ForegroundColor Yellow

    if (Test-Path $devLogDir) {
        try {
            $currentLogResolved = (Resolve-Path $logDir -ErrorAction SilentlyContinue).Path
            $devLogResolved = (Resolve-Path $devLogDir -ErrorAction SilentlyContinue).Path
            if ($currentLogResolved -ne $devLogResolved) {
                Copy-Item -Path $csvFile -Destination $devLogDir -Force
                Write-Host " Copia sincronizzata in: $devLogDir" -ForegroundColor DarkGray
            }
        }
        catch { }
    }
    Write-Host "==========================================================" -ForegroundColor Cyan
}
