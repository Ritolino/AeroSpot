<#
.SYNOPSIS
    Compila il launcher nativo standalone AeroSpot.exe usando csc.exe del .NET Framework (incluso in Windows 10/11).
.DESCRIPTION
    Produce un eseguibile leggero (~20 KB), senza dipendenze dal runtime .NET Core / .NET 10 globale,
    dotato di icona AeroSpot ufficiale e metadati assembly.
#>
param(
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path $csc)) {
    Write-Error "Compilatore csc.exe non trovato in C:\Windows\Microsoft.NET!"
}

$sourceFile = Join-Path $repoRoot "src\AeroSpot.Launcher\Program.cs"
$iconFile = Join-Path $repoRoot "design\app.ico"

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $repoRoot "bin\Release\publish\AeroSpot.exe"
}

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

Write-Host "Compilazione launcher nativo AeroSpot.exe..." -ForegroundColor Cyan
& $csc /target:winexe /win32icon:$iconFile /r:System.Windows.Forms.dll /out:$OutFile $sourceFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilazione del launcher AeroSpot.exe fallita!"
}

Write-Host "Launcher generato con successo: $OutFile ($( [Math]::Round((Get-Item $OutFile).Length / 1KB, 1) ) KB)" -ForegroundColor Green
