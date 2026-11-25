<#
.SYNOPSIS
    Compila il launcher nativo standalone AeroSpot.exe usando csc.exe del .NET Framework (incluso in Windows 10/11).
.DESCRIPTION
    Produce un eseguibile leggero (~20 KB), senza dipendenze dal runtime .NET Core / .NET 10 globale,
    dotato di icona AeroSpot ufficiale e metadati assembly.
#>
param(
    [string]$OutFile = "",
    [string]$Version = ""
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
$iconFile = Join-Path $repoRoot "assets\app.ico"

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $repoRoot "bin\Release\publish\AeroSpot.exe"
}

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$compileSource = $sourceFile
$tempSource = $null
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $cleanVer = ($Version -split '[-+]')[0].Trim()
    $parts = @($cleanVer.Split('.') | Where-Object { $_ -match '^\d+$' })
    if ($parts.Count -ge 1) {
        while ($parts.Count -lt 4) { $parts += '0' }
        $assemblyVer = ($parts[0..3]) -join '.'
        $tempSource = Join-Path ([System.IO.Path]::GetTempPath()) ("AeroSpot_Launcher_" + [Guid]::NewGuid().ToString("N") + ".cs")
        $content = Get-Content -Path $sourceFile -Raw -Encoding UTF8
        $content = [Regex]::Replace($content, '\[assembly:\s*AssemblyVersion\("[^"]*"\)\]', "[assembly: AssemblyVersion(`"$assemblyVer`")]")
        $content = [Regex]::Replace($content, '\[assembly:\s*AssemblyFileVersion\("[^"]*"\)\]', "[assembly: AssemblyFileVersion(`"$assemblyVer`")]")
        [System.IO.File]::WriteAllText($tempSource, $content, [System.Text.UTF8Encoding]::new($false))
        $compileSource = $tempSource
    }
}

Write-Host "Compilazione launcher nativo AeroSpot.exe..." -ForegroundColor Cyan
try {
    & $csc /target:winexe /win32icon:$iconFile /r:System.Windows.Forms.dll /out:$OutFile $compileSource
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Compilazione del launcher AeroSpot.exe fallita!"
    }
}
finally {
    if ($tempSource -and (Test-Path $tempSource)) {
        Remove-Item -Force $tempSource -ErrorAction SilentlyContinue
    }
}

Write-Host "Launcher generato con successo: $OutFile ($( [Math]::Round((Get-Item $OutFile).Length / 1KB, 1) ) KB)" -ForegroundColor Green
