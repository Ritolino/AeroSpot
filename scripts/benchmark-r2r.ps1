$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$dirNoR2R = Join-Path $repoRoot 'bin\Release\publish-no-r2r'
$dirR2R = Join-Path $repoRoot 'bin\Release\publish-with-r2r'

# 1. Spazio su disco
$sizeNoR2R = (Get-ChildItem -Recurse $dirNoR2R | Measure-Object -Property Length -Sum).Sum
$sizeR2R = (Get-ChildItem -Recurse $dirR2R | Measure-Object -Property Length -Sum).Sum

$sizeNoR2R_MB = [Math]::Round($sizeNoR2R / 1MB, 2)
$sizeR2R_MB = [Math]::Round($sizeR2R / 1MB, 2)

# 2. Compressione Zip
$zipNoR2R = Join-Path $repoRoot 'bin\Release\AeroSpot_no-r2r.zip'
$zipR2R = Join-Path $repoRoot 'bin\Release\AeroSpot_with-r2r.zip'

if (Test-Path $zipNoR2R) { Remove-Item -Force $zipNoR2R }
if (Test-Path $zipR2R) { Remove-Item -Force $zipR2R }

Compress-Archive -Path "$dirNoR2R\*" -DestinationPath $zipNoR2R -CompressionLevel Optimal
Compress-Archive -Path "$dirR2R\*" -DestinationPath $zipR2R -CompressionLevel Optimal

$zipSizeNoR2R_MB = [Math]::Round((Get-Item $zipNoR2R).Length / 1MB, 2)
$zipSizeR2R_MB = [Math]::Round((Get-Item $zipR2R).Length / 1MB, 2)

# 3. Benchmark Avvio a Freddo del Worker (10 iterazioni)
function Measure-WorkerStartup($exePath) {
    $times = @()
    for ($i = 0; $i -lt 10; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $exePath -ArgumentList '--benchmark-cold-start' -NoNewWindow -PassThru -Wait
        $sw.Stop()
        $times += $sw.ElapsedMilliseconds
        Start-Sleep -Milliseconds 150
    }
    $sorted = $times | Sort-Object
    $min = $sorted[0]
    $max = $sorted[-1]
    $med = $sorted[5]
    return [PSCustomObject]@{
        Min = $min
        Median = $med
        Max = $max
        All = ($sorted -join ', ')
    }
}

$benchNoR2R = Measure-WorkerStartup "$dirNoR2R\AeroSpot.Worker.exe"
$benchR2R = Measure-WorkerStartup "$dirR2R\AeroSpot.Worker.exe"

Write-Output "=== RISULTATI CONFRONTO READYTORUN ==="
Write-Output "Senza ReadyToRun:"
Write-Output "  Spazio su disco: $sizeNoR2R_MB MB ($sizeNoR2R bytes)"
Write-Output "  Dimensione Zip:  $zipSizeNoR2R_MB MB"
Write-Output "  Avvio Worker:    Min=$($benchNoR2R.Min)ms, Mediana=$($benchNoR2R.Median)ms, Max=$($benchNoR2R.Max)ms"
Write-Output "  Misure:          $($benchNoR2R.All)"

Write-Output "
Con ReadyToRun:"
Write-Output "  Spazio su disco: $sizeR2R_MB MB ($sizeR2R bytes)"
Write-Output "  Dimensione Zip:  $zipSizeR2R_MB MB"
Write-Output "  Avvio Worker:    Min=$($benchR2R.Min)ms, Mediana=$($benchR2R.Median)ms, Max=$($benchR2R.Max)ms"
Write-Output "  Misure:          $($benchR2R.All)"
