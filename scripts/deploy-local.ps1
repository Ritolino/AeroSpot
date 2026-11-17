<#
.SYNOPSIS
    Distribuisce la build Release di AeroSpot nella cartella runtime standalone locale su SSD (C:\Programmi_miei\AeroSpot).
.DESCRIPTION
    1. Arresta in sicurezza eventuali processi attivi (AeroSpot.Host, AeroSpot.Worker).
    2. Se necessario, esegue build-release.ps1 per rigenerare i binari pubblicati.
    3. Copia i binari da bin\Release\publish verso C:\Programmi_miei\AeroSpot preservando rigorosamente la cartella Data\ (database SQLite, indici e impostazioni utente).
    4. Riavvia AeroSpot.exe dal nuovo percorso.
.PARAMETER TargetDir
    Cartella di destinazione (predefinito: C:\Programmi_miei\AeroSpot).
.PARAMETER SkipBuild
    Salta la ricompilazione e copia direttamente i binari già presenti in bin\Release\publish.
.PARAMETER NoRestart
    Non riavvia l'applicazione al termine della copia.
#>
param(
    [string]$TargetDir = "C:\Programmi_miei\AeroSpot",
    [switch]$SkipBuild,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publishDir = Join-Path $repoRoot "bin\Release\publish"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AeroSpot - Deploy Locale su SSD                        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Sorgente Publish: $publishDir"
Write-Host "Destinazione SSD: $TargetDir"
Write-Host "=========================================================="

# 1. Arresta processi in esecuzione prima della copia
Write-Host "`n[1/4] Chiusura processi attivi di AeroSpot..." -ForegroundColor Yellow
Stop-Process -Name "AeroSpot.Host", "AeroSpot.Worker" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

# 2. Compilazione se richiesta o se publish non esiste
if (-not $SkipBuild -or -not (Test-Path $publishDir)) {
    Write-Host "`n[2/4] Esecuzione build Release (-SkipTests)..." -ForegroundColor Yellow
    & "$repoRoot\scripts\build-release.ps1" -SkipTests -NoZip
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Compilazione fallita. Distribuzione interrotta."
    }
} else {
    Write-Host "`n[2/4] Compilazione saltata (uso binari publish esistenti)." -ForegroundColor DarkGray
}

# 3. Creazione directory target se non esiste
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}

# 4. Copia con robocopy escludendo categoricamente Data\
Write-Host "`n[3/4] Copia binari e asset (preservando Data\)..." -ForegroundColor Yellow
& robocopy $publishDir $TargetDir /E /XD "Data" /NP /NFL /NDL /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -gt 7) {
    Write-Error "Robocopy ha riscontrato un errore fatale (ExitCode: $LASTEXITCODE)."
}
Write-Host "Copia completata con successo. La cartella Data\ è intatta." -ForegroundColor Green

# 5. Riavvio applicazione da SSD
if (-not $NoRestart) {
    Write-Host "`n[4/4] Avvio di AeroSpot.exe da $TargetDir..." -ForegroundColor Yellow
    $launcher = Join-Path $TargetDir "AeroSpot.exe"
    if (Test-Path $launcher) {
        Start-Process "explorer.exe" $launcher
        Start-Sleep -Seconds 2
        $running = Get-Process -Name "AeroSpot*" -ErrorAction SilentlyContinue
        if ($running) {
            Write-Host "AeroSpot avviato con successo su SSD (PID: $($running.Id -join ', '))." -ForegroundColor Green
        }
    }
}

Write-Host "`nDeploy completato con successo in $TargetDir!" -ForegroundColor Cyan
