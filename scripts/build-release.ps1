<#
.SYNOPSIS
    Compila e pubblica AeroSpot in modalità Self-Contained per Windows (win-x64).
.DESCRIPTION
    Esegue i test di regressione, aggiorna la documentazione HTML offline, compila
    AeroSpot.Host e AeroSpot.Worker nella medesima cartella di distribuzione (runtime condiviso)
    e genera il pacchetto zip portable pronto al rilascio con checksum SHA-256.
.PARAMETER Configuration
    Configurazione di compilazione (predefinito: Release).
.PARAMETER Runtime
    Target Runtime Identifier (predefinito: win-x64).
.PARAMETER Version
    Versione di release (predefinito: 1.0.0).
.PARAMETER ReadyToRun
    Se attivare la precompilazione AOT ReadyToRun per eliminare i lag di avvio JIT (predefinito: $true).
.PARAMETER OutDir
    Cartella di destinazione dei binari pubblicati.
.PARAMETER SkipTests
    Se saltare l'esecuzione preventiva dei test unitari (predefinito: $false).
.PARAMETER CreateZip
    Se generare l'archivio ZIP pronto alla distribuzione (predefinito: $true).
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [bool]$ReadyToRun = $true,
    [string]$OutDir = "",
    [switch]$SkipTests,
    [bool]$CreateZip = $true
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$dotnet = "dotnet"
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repoRoot "bin\$Configuration\publish"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AeroSpot - Procedura di Pubblicazione e Build Release  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Versione:       $Version"
Write-Host "Configurazione: $Configuration"
Write-Host "Runtime Target: $Runtime"
Write-Host "ReadyToRun:     $ReadyToRun"
Write-Host "Cartella Out:   $OutDir"
Write-Host "Root Repo:      $repoRoot"
Write-Host "=========================================================="

# 1. Aggiornamento Documentazione HTML Offline
Write-Host "`n[1/5] Generazione documentazione HTML offline..." -ForegroundColor Yellow
powershell -ExecutionPolicy Bypass -File "$repoRoot\scripts\make-instructions-html.ps1"

# 2. Esecuzione Test di Regressione
if (-not $SkipTests) {
    Write-Host "`n[2/5] Esecuzione suite test completa..." -ForegroundColor Yellow
    & $dotnet test "$repoRoot\AeroSpot.sln" -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test falliti! La compilazione di rilascio è stata interrotta."
    }
    Write-Host "Tutti i test sono stati superati con successo." -ForegroundColor Green
} else {
    Write-Host "`n[2/5] Test ignorati su richiesta (-SkipTests)." -ForegroundColor DarkGray
}

# 3. Pulizia e Preparazione Cartella di Destinazione
Write-Host "`n[3/5] Pulizia cartella di destinazione: $OutDir" -ForegroundColor Yellow
if (Test-Path $OutDir) {
    Remove-Item -Recurse -Force $OutDir
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$appDir = Join-Path $OutDir "app"
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

# 4. Pubblicazione Host e Worker (Sottocartella app/, runtime condiviso)
Write-Host "`n[4/5] Pubblicazione AeroSpot.Host (Self-Contained in app/)..." -ForegroundColor Yellow
$pArgR2R = if ($ReadyToRun) { "true" } else { "false" }

& $dotnet publish "$repoRoot\src\AeroSpot.Host\AeroSpot.Host.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishReadyToRun=$pArgR2R `
    -p:PublishSingleFile=false `
    -o $appDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Pubblicazione di AeroSpot.Host fallita!"
}

Write-Host "`n[4/5] Pubblicazione AeroSpot.Worker (Self-Contained in app/, runtime condiviso)..." -ForegroundColor Yellow
& $dotnet publish "$repoRoot\src\AeroSpot.Worker\AeroSpot.Worker.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishReadyToRun=$pArgR2R `
    -p:PublishSingleFile=false `
    -o $appDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Pubblicazione di AeroSpot.Worker fallita!"
}

# Rimozione simboli di debug .pdb
Get-ChildItem -Path $appDir -Filter "*.pdb" -Recurse | Remove-Item -Force

# Rimozione cartelle satelliti lingue .NET/WPF non necessarie (AeroSpot gestisce localizzazione EN/IT in-memory)
$langCodes = @("cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant")
foreach ($lc in $langCodes) {
    $sub = Join-Path $appDir $lc
    if (Test-Path $sub) { Remove-Item -Recurse -Force $sub }
}

# Rimozione Docs ridondante dentro app/ se presente
$appDocs = Join-Path $appDir "Docs"
if (Test-Path $appDocs) { Remove-Item -Recurse -Force $appDocs }

# Inclusione file portabilità dentro app/
New-Item -ItemType File -Path (Join-Path $appDir "portable.txt") -Force | Out-Null

$licenseCandidate = Join-Path (Split-Path -Parent $repoRoot) "GitHub\LICENSE"
if (Test-Path $licenseCandidate) {
    Copy-Item -Path $licenseCandidate -Destination (Join-Path $appDir "LICENSE") -Force
}

# Compilazione del Launcher nativo pulito AeroSpot.exe nella radice
$launcherExe = Join-Path $OutDir "AeroSpot.exe"
powershell -ExecutionPolicy Bypass -File "$repoRoot\scripts\compile-launcher.ps1" -OutFile $launcherExe

$readmeContent = @"
AeroSpot v$Version (Portable Standalone)
=====================================
Avvio rapido: Doppio clic su 'AeroSpot.exe'
Scorciatoia di ricerca: Alt + Spazio
Anteprima QuickLook: Spazio
Chiusura: Clic destro sull'icona nella barra di notifica -> 'Esci da AeroSpot'
Documentazione: Apri 'Docs/instructions.it.html' o 'Docs/instructions.en.html'
GitHub: https://github.com/Ritolino/AeroSpot
"@
Set-Content -Path (Join-Path $OutDir "README.txt") -Value $readmeContent -Encoding utf8

$outDocs = Join-Path $OutDir "Docs"
if (-not (Test-Path $outDocs)) { New-Item -ItemType Directory -Path $outDocs -Force | Out-Null }
Copy-Item -Path "$repoRoot\Docs\instructions.it.html" -Destination $outDocs -Force
Copy-Item -Path "$repoRoot\Docs\instructions.en.html" -Destination $outDocs -Force

# Verifica file critici
$hostExe = Join-Path $appDir "AeroSpot.Host.exe"
$workerExe = Join-Path $appDir "AeroSpot.Worker.exe"
$docsIt = Join-Path $OutDir "Docs\instructions.it.html"
$docsEn = Join-Path $OutDir "Docs\instructions.en.html"

if (-not (Test-Path $launcherExe) -or -not (Test-Path $hostExe) -or -not (Test-Path $workerExe) -or -not (Test-Path $docsIt) -or -not (Test-Path $docsEn)) {
    Write-Error "Verifica integrità fallita: uno o più file indispensabili non sono presenti nell'output pubblicato!"
}

$totalSize = (Get-ChildItem -Recurse -File $OutDir | Measure-Object -Property Length -Sum).Sum
$totalSizeMb = [Math]::Round($totalSize / 1MB, 2)
Write-Host "`nPubblicazione completata con successo!" -ForegroundColor Green
Write-Host "Dimensione totale cartella non compressa: $totalSizeMb MB ($totalSize bytes)"

# 5. Generazione Archivio ZIP e Checksum SHA-256
if ($CreateZip) {
    Write-Host "`n[5/5] Compressione archivio ZIP di distribuzione..." -ForegroundColor Yellow
    $parent = Split-Path -Parent $repoRoot
    $candidate = Join-Path $parent "GitHub\release"
    $distDir = if (Test-Path (Split-Path -Parent $candidate)) { $candidate } else { Join-Path $repoRoot "GitHub\release" }
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    }

    $zipName = "AeroSpot-v$Version-$Runtime-portable.zip"
    $zipPath = Join-Path $distDir $zipName
    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $zipSize = (Get-Item $zipPath).Length
    $zipSizeMb = [Math]::Round($zipSize / 1MB, 2)
    Write-Host "Archivio creato: $zipPath" -ForegroundColor Green
    Write-Host "Dimensione archivio ZIP: $zipSizeMb MB ($zipSize bytes)"

    # Calcolo SHA-256
    $hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $shaEntry = "$hash  $zipName"
    Set-Content -Path (Join-Path $distDir "SHA256SUMS.txt") -Value $shaEntry -Encoding utf8
    Write-Host "Checksum generato: $shaEntry" -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   Procedura di Build Release Conclusa con Successo!     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
