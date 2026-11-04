<#
.SYNOPSIS
    The smoke gate for `jankscope probe --json` output, kept in one function so it can be tested (tools/dev/test-smoke-gate.ps1).

.DESCRIPTION
    A probe that throws or times out still becomes a row, so the matrix looks complete. This gate fails on a failed probe, on a probe
    that did not report, and on a matrix that is too small to be real. Throws on failure; returns the summary line on success.
#>
function Assert-ProbeMatrix {
    param(
        [Parameter(Mandatory)]$Matrix,
        [string[]]$ExpectedProbes = @('os', 'privilege', 'etw', 'security', 'hardware', 'managed', 'anticheat', 'game', 'cpu', 'gpu', 'display', 'environment', 'pdh', 'planned'),
        [string[]]$ExpectedRows = @('os.version', 'os.support', 'privilege.elevation', 'etw.realtime_session', 'anticheat.state', 'frame.capture', 'mode.optimize', 'gpu.adapters', 'cpu.topology', 'display.topology', 'environment.virtual_machine',
            'managed.applied_policies', 'managed.entra_join', 'game.session',
            'pdh.cpu_utility', 'pdh.cpu_frequency', 'pdh.memory', 'pdh.hard_faults', 'pdh.disk', 'pdh.gpu_engine', 'pdh.gpu_memory')
    )

    if (-not $Matrix.entries -or @($Matrix.entries).Count -lt 10) { throw 'Probe produced no capability matrix.' }
    $health = @($Matrix.probeHealth)
    $failed = @($health | Where-Object { -not $_.succeeded })
    if ($failed.Count -gt 0) {
        throw ('Probe(s) failed: ' + (($failed | ForEach-Object { "$($_.probeId) ($($_.error))" }) -join ', '))
    }
    $missing = @($ExpectedProbes | Where-Object { $health.probeId -notcontains $_ })
    if ($missing.Count -gt 0) { throw ('Expected probe(s) did not report: ' + ($missing -join ', ')) }
    # A probe can succeed with an empty list; the rows the rest of the system depends on must exist.
    $ids = @($Matrix.entries | ForEach-Object { $_.id })
    $absent = @($ExpectedRows | Where-Object { $ids -notcontains $_ })
    if ($absent.Count -gt 0) { throw ('Expected row(s) missing from the matrix: ' + ($absent -join ', ')) }
    return ('{0} capability rows, {1} probes, 0 failed' -f @($Matrix.entries).Count, $health.Count)
}
