<#
.SYNOPSIS
    One-command build, test and smoke run for Jankscope.

.EXAMPLE
    ./build.ps1                 # restore, build (Release), run all tests, smoke-run the CLI
    ./build.ps1 -Clean          # delete bin/obj first (use this for a clean-checkout check)
    ./build.ps1 -Configuration Debug -NoSmoke
    ./build.ps1 -RequireHardwareTests   # on the reference bench: a Direct3D or PresentMon test that would skip fails instead

.NOTES
    Works in Windows PowerShell 5.1 and PowerShell 7. Requires the .NET SDK pinned in global.json.
    Nothing here installs software or touches the network except NuGet restore.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$NoTest,
    [switch]$NoSmoke,
    [switch]$RequireHardwareTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Set-Location $PSScriptRoot

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $fallback) { return $fallback }
    throw "The .NET SDK was not found. Install the version pinned in global.json (see docs/INSTALL.md)."
}

$stepTimings = New-Object System.Collections.Generic.List[pscustomobject]
$isCi = ($env:GITHUB_ACTIONS -eq 'true')

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    if ($isCi) { Write-Host "::group::$Name" }
    Write-Host "==> $Name" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        if ($LASTEXITCODE -ne 0) { throw "Step failed: $Name (exit code $LASTEXITCODE)" }
        $sw.Stop()
        $script:stepTimings.Add([pscustomobject]@{ Step = $Name; Status = 'Passed'; Seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1) })
    } catch {
        $sw.Stop()
        $script:stepTimings.Add([pscustomobject]@{ Step = $Name; Status = 'Failed'; Seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1) })
        throw
    } finally {
        if ($isCi) { Write-Host "::endgroup::" }
    }
}

$dotnet = Find-Dotnet
$pinned = (Get-Content (Join-Path $PSScriptRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$actual = (& $dotnet --version).Trim()
Write-Host "dotnet $actual (global.json pins $pinned)"
if (-not $actual.StartsWith('10.')) { throw "Expected a .NET 10 SDK, found $actual." }

if ($Clean) {
    Write-Host "==> Clean" -ForegroundColor Cyan
    # Never reach into another checkout nested in this one (an agent worktree under .claude\worktrees has its own .git entry): its build output is not ours to delete.
    function Test-InNestedCheckout([string]$Path, [string]$Root) {
        for ($dir = Split-Path -Parent $Path; $dir -and $dir.Length -gt $Root.Length; $dir = Split-Path -Parent $dir) {
            if (Test-Path -LiteralPath (Join-Path $dir '.git')) { return $true }
        }
        return $false
    }

    Get-ChildItem -Path $PSScriptRoot -Include bin, obj -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\(src|tools|tests)\\' } |
        Where-Object { -not (Test-InNestedCheckout $_.FullName $PSScriptRoot) } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
}

Invoke-Step 'Restore'  { & $dotnet restore Jankscope.slnx }
Invoke-Step 'Build'    { & $dotnet build Jankscope.slnx -c $Configuration --no-restore }

# The banned-API analyzer silently ignores a method entry whose signature does not match; this proves every entry really fires.
Invoke-Step 'Verify banned-symbol coverage' { & (Join-Path $PSScriptRoot 'tools\dev\verify-banned-symbols.ps1') -Dotnet $dotnet }
# ...and that every product project actually loads the analyzer and the list (asked of MSBuild, not read from text).
Invoke-Step 'Verify analyzer wiring' { & (Join-Path $PSScriptRoot 'tools\dev\verify-analyzer-wiring.ps1') -Dotnet $dotnet }

if (-not $NoTest) {
    # Leftovers of an interrupted --blame-hang-timeout run (Sequence_*.xml) would make the encoding lint fail until deleted by hand.
    Get-ChildItem -Path (Join-Path $PSScriptRoot 'tests') -Directory -Recurse -Filter TestResults -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
    if ($RequireHardwareTests) {
        # A test that needs a Direct3D 11 device or the real PresentMon skips itself with a reason where it cannot run. On the reference bench that
        # must not pass silently (review 1a, finding 7): these variables make the canary tests in Jankscope.Harness.Tests and Jankscope.Frames.Tests fail instead.
        $env:JANKSCOPE_REQUIRE_D3D = '1'
        $env:JANKSCOPE_REQUIRE_PRESENTMON = '1'
        Write-Host "    hardware tests required: a skipped Direct3D or PresentMon test fails the run"
    }

    # One test project at a time (-m:1): the suite contains timing tests (frame pacing, sampler drift, watchdogs, concurrency stress) that
    # are only meaningful without CPU contention from sibling projects. A hung test host is aborted and named after 5 minutes instead of
    # stalling the build (--blame-hang-timeout).
    Invoke-Step 'Test' { & $dotnet test Jankscope.slnx -c $Configuration --no-build -m:1 --blame-hang-timeout 300s --blame-hang-dump-type none }
}

if (-not $NoSmoke) {
    $cli = Join-Path $PSScriptRoot "src\Jankscope.Cli\bin\$Configuration\net10.0-windows\jankscope.exe"
    # The gate (tools/dev/Assert-ProbeMatrix.ps1) fails on a failed or missing probe; its own self-test proves that it can fail.
    Invoke-Step 'Verify smoke gate' { & (Join-Path $PSScriptRoot 'tools\dev\test-smoke-gate.ps1') }
    . (Join-Path $PSScriptRoot 'tools\dev\Assert-ProbeMatrix.ps1')
    Invoke-Step 'Smoke: jankscope probe --json' {
        $json = & $cli probe --json
        if ($LASTEXITCODE -ne 0) { return }
        $matrix = $json | Out-String | ConvertFrom-Json
        $script:smokeSummary = Assert-ProbeMatrix -Matrix $matrix
        Write-Host ("    " + $script:smokeSummary)
        $global:LASTEXITCODE = 0
    }
}

if ($env:GITHUB_STEP_SUMMARY -and (Test-Path (Split-Path -Parent $env:GITHUB_STEP_SUMMARY))) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('### Jankscope CI Verification Summary')
    $lines.Add('')
    $lines.Add("| Step | Status | Duration (s) |")
    $lines.Add("| :--- | :--- | ---: |")
    foreach ($row in $stepTimings) {
        $lines.Add("| $($row.Step) | $($row.Status) | $($row.Seconds.ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture)) |")
    }
    if ($script:smokeSummary) {
        $lines.Add('')
        $lines.Add(('**Smoke Gate (`jankscope probe --json`):** `' + $script:smokeSummary + '`'))
    }
    $lines | Set-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}

Write-Host "==> OK" -ForegroundColor Green
