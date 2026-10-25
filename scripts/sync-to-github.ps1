<#
.SYNOPSIS
    Sincronizza in modo sicuro i file di sviluppo di AeroSpot nella cartella pubblica GitHub\.
.DESCRIPTION
    La cartella radice di sviluppo è la fonte di verità. Questo script aggiorna GitHub\
    copiando esclusivamente i sorgenti puliti (senza bin/obj), i test, gli script,
    la documentazione pubblica e i file di configurazione di repository.
    Non tocca mai la cartella .git\ né la cartella dist\.
.PARAMETER WhatIf
    Esegue una simulazione visualizzando le operazioni senza apportare modifiche.
.PARAMETER TargetDir
    Cartella di destinazione (predefinito: ..\GitHub).
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$TargetDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $parent = Split-Path -Parent $repoRoot
    $candidate = Join-Path $parent "GitHub"
    if (Test-Path $candidate) {
        $TargetDir = $candidate
    } else {
        $TargetDir = Join-Path $repoRoot "GitHub"
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AeroSpot - Sincronizzazione Repository GitHub         " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Sorgente (Sviluppo): $repoRoot"
Write-Host "Destinazione:        $TargetDir"
Write-Host "Modalità WhatIf:     $($PSCmdlet.MyInvocation.BoundParameters.ContainsKey('WhatIf'))"
Write-Host "=========================================================="

if (-not (Test-Path $TargetDir)) {
    if ($PSCmdlet.ShouldProcess($TargetDir, "Crea directory")) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }
}

# 1. File Radice Gestiti da Progetto
$rootFiles = @(
    "AeroSpot.sln",
    "Directory.Build.props"
)

Write-Host "`n[1/4] Sincronizzazione file radice gestiti..." -ForegroundColor Yellow
foreach ($file in $rootFiles) {
    $srcFile = Join-Path $repoRoot $file
    $dstFile = Join-Path $TargetDir $file
    if (Test-Path $srcFile) {
        if ($PSCmdlet.ShouldProcess("$srcFile -> $dstFile", "Copia file radice")) {
            Copy-Item -Path $srcFile -Destination $dstFile -Force
            Write-Host "  Copiato: $file" -ForegroundColor Green
        }
    } else {
        Write-Host "  Non trovato (saltato): $file" -ForegroundColor DarkGray
    }
}

# 2. Funzione di copia cartella con esclusione bin/obj/.git/dist
function Sync-FolderContent {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [string]$FolderName
    )

    Write-Host "`n[Sincronizzazione] $FolderName..." -ForegroundColor Yellow

    if (-not (Test-Path $SourcePath)) {
        Write-Host "  Cartella sorgente assente: $SourcePath" -ForegroundColor DarkGray
        return
    }

    if (-not (Test-Path $DestinationPath)) {
        if ($PSCmdlet.ShouldProcess($DestinationPath, "Crea directory $FolderName")) {
            New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
        }
    }

    # Utilizzo Robocopy in mirroring con esclusioni rigorose
    $roboArgs = @(
        $SourcePath,
        $DestinationPath,
        "/MIR",
        "/XD", "bin", "obj", ".git", "dist", "release", ".vs", ".vscode", "Data", "screenshots",
        "/XF", "*.user", "*.suo", "*.db", "*.db-wal", "*.db-shm", "*.log",
        "/R:1", "/W:1",
        "/NP", "/NDL"
    )

    if ($PSCmdlet.MyInvocation.BoundParameters.ContainsKey('WhatIf')) {
        $roboArgs += "/L"
    }

    $res = & robocopy @roboArgs
    # Robocopy exit code < 8 indica successo (0 = no change, 1 = files copied, etc.)
    if ($LASTEXITCODE -ge 8) {
        Write-Error "Errore robocopy durante la sincronizzazione di $FolderName (ExitCode: $LASTEXITCODE)"
    } else {
        Write-Host "  $FolderName sincronizzato con successo." -ForegroundColor Green
    }
}

# 3. Sincronizzazione src, tests, scripts
Sync-FolderContent -SourcePath (Join-Path $repoRoot "src") -DestinationPath (Join-Path $TargetDir "src") -FolderName "src"
Sync-FolderContent -SourcePath (Join-Path $repoRoot "tests") -DestinationPath (Join-Path $TargetDir "tests") -FolderName "tests"
Sync-FolderContent -SourcePath (Join-Path $repoRoot "scripts") -DestinationPath (Join-Path $TargetDir "scripts") -FolderName "scripts"

# 4. Sincronizzazione Docs -> docs
Sync-FolderContent -SourcePath (Join-Path $repoRoot "Docs") -DestinationPath (Join-Path $TargetDir "docs") -FolderName "docs"

# 5. Assicura presenza cartelle di pubblicazione e sincronizza screenshot in modo sicuro (non distruttivo)
$srcScreenshots = Join-Path $repoRoot "Docs\screenshots"
$screenshotsDir = Join-Path $TargetDir "docs\screenshots"
if (-not (Test-Path $screenshotsDir)) {
    if ($PSCmdlet.ShouldProcess($screenshotsDir, "Crea directory docs\screenshots")) {
        New-Item -ItemType Directory -Path $screenshotsDir -Force | Out-Null
    }
}
if (Test-Path $srcScreenshots) {
    # Copia incrementale senza eliminare screenshot aggiunti manualmente dall'utente
    & robocopy $srcScreenshots $screenshotsDir /E /XO /NP /NDL /R:1 /W:1 | Out-Null
}

$releaseDir = Join-Path $TargetDir "release"
if (-not (Test-Path $releaseDir)) {
    if ($PSCmdlet.ShouldProcess($releaseDir, "Crea directory release")) {
        New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    }
}

Write-Host "`nSincronizzazione completata." -ForegroundColor Green
