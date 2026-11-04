<#
.SYNOPSIS
    Paired interleaved A-B-A-B observer-effect overhead measurement (MASTER_PROMPT_v4.md section 14 and section 18).
.DESCRIPTION
    Runs identical workloads with collectors OFF (condition A) vs collectors ON (condition B) across interleaved pairs.
    Measures observer effect via harness-side QPC timestamps at Present() return, computes median and IQR,
    and evaluates pass/fail against the section 14 budgets dynamically.
#>
param(
    [int]$Frames = 300,
    [double]$Fps = 144.0,
    [int]$Pairs = 5,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\docs\benchmarks')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$harnessExe = Join-Path $repoRoot 'tools\Jankscope.Harness\bin\Release\net10.0-windows\jankscope-harness.exe'
$presentMonExe = Join-Path $repoRoot 'third_party\PresentMon\2.6.0\PresentMon-2.6.0-x64.exe'

if (-not (Test-Path $harnessExe)) {
    throw "Harness executable not found at $harnessExe. Run 'build.ps1' first."
}
if (-not (Test-Path $presentMonExe)) {
    throw "PresentMon console executable not found at $presentMonExe."
}
if ($Pairs -lt 1) {
    throw "Pairs must be at least 1."
}

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    if ($sorted.Count -eq 1) { return [double]$sorted[0] }
    $pos = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lo = [int][Math]::Floor($pos)
    $hi = [int][Math]::Ceiling($pos)
    if ($lo -eq $hi) { return [double]$sorted[$lo] }
    $frac = $pos - $lo
    return [double]$sorted[$lo] + $frac * ([double]$sorted[$hi] - [double]$sorted[$lo])
}

function Get-Median([double[]]$Values) {
    return Get-Percentile $Values 50.0
}

function Get-Iqr([double[]]$Values) {
    return (Get-Percentile $Values 75.0) - (Get-Percentile $Values 25.0)
}

function Run-Condition([string]$Label, [bool]$CollectorsOn) {
    $tempCsv = Join-Path $env:TEMP ("overhead_" + $Label + "_" + [Guid]::NewGuid().ToString('N') + ".csv")
    $summaryJson = [System.IO.Path]::ChangeExtension($tempCsv, ".summary.json")

    $pmProcess = $null
    $sessionName = "Jankscope-ovh-" + (Get-Random -Minimum 1000 -Maximum 9999)

    if ($CollectorsOn) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $presentMonExe
        $psi.Arguments = "--session_name $sessionName --process_name jankscope-harness.exe --output_stdout --terminate_after_timed --timed 30 --stop_existing_session"
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $pmProcess = [System.Diagnostics.Process]::Start($psi)
        $null = $pmProcess.StandardOutput.ReadToEndAsync()
        $null = $pmProcess.StandardError.ReadToEndAsync()
        Start-Sleep -Milliseconds 500
    }

    try {
        $null = & $harnessExe run --seed 42 --fps $Fps --frames $Frames --hitches 0 --extra-ms 0 --jitter-ms 0.1 --out $tempCsv --window-visible
        $summary = Get-Content $summaryJson -Raw | ConvertFrom-Json
        return [PSCustomObject]@{
            Label = $Label
            CollectorsOn = $CollectorsOn
            Summary = $summary
        }
    }
    finally {
        if ($pmProcess -ne $null) {
            if (-not $pmProcess.HasExited) {
                try { $pmProcess.Kill() } catch { }
            }
            $pmProcess.Dispose()
            $prevEap = $ErrorActionPreference
            $ErrorActionPreference = 'SilentlyContinue'
            try {
                & $presentMonExe --terminate_existing_session --session_name $sessionName *>$null
            } catch { }
            $ErrorActionPreference = $prevEap
        }
        Remove-Item $tempCsv, $summaryJson -ErrorAction SilentlyContinue
    }
}

$totalRuns = $Pairs * 2
Write-Host "Running paired A-B-A-B observer-effect benchmark ($Pairs pairs = $totalRuns runs, $Frames frames @ $Fps fps)..."
$runsA = @()
$runsB = @()
$allRuns = @()

for ($i = 1; $i -le $Pairs; $i++) {
    $rA = Run-Condition ("A" + $i) $false
    Write-Host ("  A" + $i + " (Collectors OFF) complete.")
    $runsA += $rA
    $allRuns += $rA

    $rB = Run-Condition ("B" + $i) $true
    Write-Host ("  B" + $i + " (Collectors ON) complete.")
    $runsB += $rB
    $allRuns += $rB
}

$aPresentMedians = [double[]]@($runsA | ForEach-Object { [double]$_.Summary.fidelity.presentTimeErrorMs.median })
$bPresentMedians = [double[]]@($runsB | ForEach-Object { [double]$_.Summary.fidelity.presentTimeErrorMs.median })
$aPresentP99s    = [double[]]@($runsA | ForEach-Object { [double]$_.Summary.fidelity.presentTimeErrorMs.p99 })
$bPresentP99s    = [double[]]@($runsB | ForEach-Object { [double]$_.Summary.fidelity.presentTimeErrorMs.p99 })
$aFrameMedians   = [double[]]@($runsA | ForEach-Object { [double]$_.Summary.fidelity.frameTimeErrorMs.median })
$bFrameMedians   = [double[]]@($runsB | ForEach-Object { [double]$_.Summary.fidelity.frameTimeErrorMs.median })

$aPresentMedian = Get-Median $aPresentMedians
$bPresentMedian = Get-Median $bPresentMedians
$aPresentMedianIqr = Get-Iqr $aPresentMedians
$bPresentMedianIqr = Get-Iqr $bPresentMedians
$deltaPresentMedian = $bPresentMedian - $aPresentMedian

$aPresentP99 = Get-Median $aPresentP99s
$bPresentP99 = Get-Median $bPresentP99s
$aPresentP99Iqr = Get-Iqr $aPresentP99s
$bPresentP99Iqr = Get-Iqr $bPresentP99s
$deltaPresentP99 = $bPresentP99 - $aPresentP99

$aFrameMedian = Get-Median $aFrameMedians
$bFrameMedian = Get-Median $bFrameMedians
$aFrameMedianIqr = Get-Iqr $aFrameMedians
$bFrameMedianIqr = Get-Iqr $bFrameMedians
$deltaFrameMedian = $bFrameMedian - $aFrameMedian

$limitPresentMedian = 0.200
$limitPresentP99 = 0.500
$limitFrameMedian = 0.100

$passPresentMedian = [Math]::Abs($deltaPresentMedian) -lt $limitPresentMedian
$passPresentP99 = [Math]::Abs($deltaPresentP99) -lt $limitPresentP99
$passFrameMedian = [Math]::Abs($deltaFrameMedian) -lt $limitFrameMedian

$statusPresentMedian = if ($passPresentMedian) { "**PASS**" } else { "**FAIL**" }
$statusPresentP99 = if ($passPresentP99) { "**PASS**" } else { "**FAIL**" }
$statusFrameMedian = if ($passFrameMedian) { "**PASS**" } else { "**FAIL**" }

$rows = ($allRuns | ForEach-Object {
    $mode = if ($_.CollectorsOn) { "ON" } else { "OFF" }
    $pm = ([double]$_.Summary.fidelity.presentTimeErrorMs.median).ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture)
    $pp = ([double]$_.Summary.fidelity.presentTimeErrorMs.p99).ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture)
    $fm = ([double]$_.Summary.fidelity.frameTimeErrorMs.median).ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture)
    $fp = ([double]$_.Summary.fidelity.frameTimeErrorMs.p99).ToString("F3", [System.Globalization.CultureInfo]::InvariantCulture)
    $df = $_.Summary.fidelity.disturbedFrames.Count
    "| **$($_.Label)** | $mode | $pm ms / $pp ms | $fm ms / $fp ms | $df |"
}) -join "`n"

$inv = [System.Globalization.CultureInfo]::InvariantCulture
$overallConclusion = if ($passPresentMedian -and $passPresentP99 -and $passFrameMedian) {
    "Real-time ETW event capture passed all observer-overhead thresholds Across $totalRuns interleaved runs ($Pairs A/B pairs)."
} else {
    "One or more observer-overhead thresholds were exceeded across $totalRuns interleaved runs ($Pairs A/B pairs)."
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$mdFile = Join-Path $OutputDir 'overhead-v1.md'

$doc = @"
# Overhead Measurement v1: Observer-Effect Paired A-B-A-B Benchmark

Per **spec section 14** (Self-overhead and self-protection) and **section 18** (Statistical rigor: 10+ interleaved runs with median and IQR):
The observer-effect benchmark compares identical workloads run with collectors **OFF** (condition A) versus collectors **ON** (condition B) across $Pairs interleaved A/B pairs ($totalRuns runs total), measured directly from the harness's independent QPC timestamps at `Present()` return.

## Benchmark Configuration
- **Hardware Bench:** Intel UHD Graphics + NVIDIA GeForce RTX 4060 Laptop GPU (10 cores / 16 threads, Windows 11 Home 26300)
- **Workload:** Direct3D 11 flip-model swapchain (`DXGI_SWAP_EFFECT_FLIP_DISCARD`), $Frames frames @ $Fps fps with 0.1 ms synthetic jitter per run ($Pairs pairs = $totalRuns interleaved runs)
- **Collector Condition B:** Pinned PresentMon console 2.6.0 running in ETW capture mode

## Results Summary (Median and IQR Across $Pairs Pairs)

| Metric | Condition A (Off, median / IQR) | Condition B (On, median / IQR) | Observer Delta (B - A) | Limit (section 14) | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Present() Return Error (Median)** | $($aPresentMedian.ToString("F3", $inv)) ms (IQR $($aPresentMedianIqr.ToString("F3", $inv))) | $($bPresentMedian.ToString("F3", $inv)) ms (IQR $($bPresentMedianIqr.ToString("F3", $inv))) | $($deltaPresentMedian.ToString("F3", $inv)) ms | < $($limitPresentMedian.ToString("F3", $inv)) ms | $statusPresentMedian |
| **Present() Return Error (p99)** | $($aPresentP99.ToString("F3", $inv)) ms (IQR $($aPresentP99Iqr.ToString("F3", $inv))) | $($bPresentP99.ToString("F3", $inv)) ms (IQR $($bPresentP99Iqr.ToString("F3", $inv))) | $($deltaPresentP99.ToString("F3", $inv)) ms | < $($limitPresentP99.ToString("F3", $inv)) ms | $statusPresentP99 |
| **Frame Time Error (Median)** | $($aFrameMedian.ToString("F3", $inv)) ms (IQR $($aFrameMedianIqr.ToString("F3", $inv))) | $($bFrameMedian.ToString("F3", $inv)) ms (IQR $($bFrameMedianIqr.ToString("F3", $inv))) | $($deltaFrameMedian.ToString("F3", $inv)) ms | < $($limitFrameMedian.ToString("F3", $inv)) ms | $statusFrameMedian |

## Individual Run Details ($totalRuns Interleaved Runs)

| Run | Collectors | Present() Error (Median / p99) | Frame Time Error (Median / p99) | Disturbed Frames |
| :--- | :--- | :--- | :--- | :--- |
$rows

## Conclusion
$overallConclusion
"@

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($mdFile, $doc, $utf8NoBom)
Write-Host "Benchmark complete! Results written to $mdFile"

if (-not ($passPresentMedian -and $passPresentP99 -and $passFrameMedian)) {
    throw "Observer overhead benchmark exceeded one or more limits."
}
