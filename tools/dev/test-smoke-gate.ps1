<#
.SYNOPSIS
    Self-test of the smoke gate (Assert-ProbeMatrix): it must pass a healthy matrix and fail each kind of bad one.
.DESCRIPTION
    Run by build.ps1 before the smoke step. Exit code 0 = the gate behaves, 1 = it does not.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-ProbeMatrix.ps1')

$ids = @('os', 'privilege', 'etw', 'security', 'hardware', 'managed', 'anticheat', 'game', 'cpu', 'gpu', 'display', 'environment', 'pdh', 'planned')
$rowIds = @('os.version', 'os.support', 'privilege.elevation', 'etw.realtime_session', 'anticheat.state', 'frame.capture', 'mode.optimize', 'gpu.adapters', 'cpu.topology', 'display.topology', 'environment.virtual_machine',
    'managed.applied_policies', 'managed.entra_join', 'game.session',
    'pdh.cpu_utility', 'pdh.cpu_frequency', 'pdh.memory', 'pdh.hard_faults', 'pdh.disk', 'pdh.gpu_engine', 'pdh.gpu_memory')
function New-Matrix([string[]]$ProbeIds, [int]$Rows = 30, [string[]]$Failing = @(), [string[]]$Without = @()) {
    [pscustomobject]@{
        entries     = @($rowIds | Where-Object { $Without -notcontains $_ }) + @(1..$Rows | ForEach-Object { "row$_" }) | ForEach-Object { [pscustomobject]@{ id = $_ } }
        probeHealth = $ProbeIds | ForEach-Object { [pscustomobject]@{ probeId = $_; succeeded = ($Failing -notcontains $_); error = $(if ($Failing -contains $_) { 'TimeoutException' } else { $null }) } }
    }
}

$problems = New-Object System.Collections.Generic.List[string]
function Expect-Pass([string]$Name, $Matrix) {
    try { $null = Assert-ProbeMatrix -Matrix $Matrix } catch { $problems.Add("$Name should pass but threw: $($_.Exception.Message)") }
}
function Expect-Fail([string]$Name, $Matrix, [string]$Pattern) {
    try {
        $null = Assert-ProbeMatrix -Matrix $Matrix
        $problems.Add("$Name should have failed but passed")
    } catch {
        if ($_.Exception.Message -notmatch $Pattern) { $problems.Add("$Name failed with an unexpected message: $($_.Exception.Message)") }
    }
}

Expect-Pass 'healthy matrix' (New-Matrix $ids)
Expect-Fail 'one failed probe' (New-Matrix $ids -Failing @('anticheat')) 'anticheat'
Expect-Fail 'a timed-out probe' (New-Matrix $ids -Failing @('hardware', 'etw')) 'hardware.*etw|etw.*hardware'
Expect-Fail 'a probe that never reported' (New-Matrix ($ids | Where-Object { $_ -ne 'managed' })) 'managed'
Expect-Fail 'too few rows' (New-Matrix $ids -Rows 0 -Without ($rowIds[0..13])) 'no capability matrix' # Rows 0 still yields two filler rows (1..0 counts down), so remove enough key rows to stay under 10
Expect-Fail 'the PDH probe succeeded but returned no rows' (New-Matrix $ids -Without @('pdh.cpu_utility', 'pdh.cpu_frequency', 'pdh.memory', 'pdh.hard_faults', 'pdh.disk', 'pdh.gpu_engine', 'pdh.gpu_memory')) 'pdh\.'
Expect-Fail 'a key row is missing (probe succeeded with an empty list)' (New-Matrix $ids -Without @('anticheat.state')) 'anticheat.state'
Expect-Fail 'no entries at all' ([pscustomobject]@{ entries = @(); probeHealth = @() }) 'no capability matrix'

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}
Write-Host '    smoke gate self-test passed (1 healthy, 7 bad matrices)'
exit 0
