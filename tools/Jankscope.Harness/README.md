# jankscope-harness: the synthetic Direct3D 11 workload

The independent ground-truth source for stutter detection (spec section 18, ADR 0006). A small renderer presents frames on a schedule that follows the output of `Jankscope.Core.SyntheticWorkload` (seed, fps, frames, hitches, extra ms, jitter) and records its **own** QPC timestamps, so detection, lows and incident anchoring can be checked against known hitches without a real game or an anti-cheat.

**Everything it writes is `Simulated`** (HC-9). It is not a measurement of any game or of this PC, the summary says so in words (`notice`, `exportableAsMeasurement: false`), every CSV row carries `JankscopeProvenance = Simulated`, and `HarnessRunResult.Provenance` is `DataProvenance.Simulated` by construction, so `RequireExportableAsMeasurement()` refuses it.

## Use

```
jankscope-harness plan [--seed N] [--fps F] [--frames N] [--hitches N] [--extra-ms M] [--jitter-ms J] [--out FILE]
jankscope-harness run  [same workload options] --out FILE [--adapter auto|hardware|warp] [--window-visible]
jankscope-harness --help
```

Defaults: seed 1, 144 fps, 3000 frames, 8 hitches of 25 ms, 0.2 ms jitter, adapter `auto`, window never shown. Option values are parsed strictly with the invariant culture; a bad value is a usage error (exit 2), never a silent default.

| Exit code | Meaning |
|---|---|
| 0 | success |
| 1 | unexpected failure |
| 2 | usage error (including an impossible workload, for example too many hitches for the run length) |
| 3 | no usable Direct3D 11 adapter or window on this machine (for example `--adapter hardware` with none) |
| 130 | cancelled with Ctrl+C; the device, swap chain and window are released and nothing is written |

## What `run` does

1. Opens a 320x180 WinForms window (`net10.0-windows`, `UseWindowsForms`). WinForms creates the window, so the harness has **no native import of its own** (no `CreateWindowEx`, no `QueryPerformanceCounter`; `Stopwatch` is the QPC wrapper). By default the window exists but is never shown; `--window-visible` shows it without taking focus.
2. Creates a Direct3D 11 device (Vortice.Direct3D11/DXGI 3.8.3) and a **flip-model swap chain** (`DXGI_SWAP_EFFECT_FLIP_DISCARD`, 3 buffers, BGRA8). Adapter choice: `hardware` = the first non-software adapter that accepts a feature-level 11.x device; `warp` = the Windows software rasterizer; `auto` = hardware, else WARP with the reason recorded in `adapter.fallbackReason`.
3. Pacing: frame *i* is due at the cumulative sum of the first *i* + 1 ground-truth frame times, counted from the pacing origin. The targets are **absolute**, so a late frame never shifts later ones. Each frame's contents (a colour clear; hitch frames flash red) are recorded before the wait, so `Present(0)` is the only call between the target time and the timestamp. `Present(0)` is called with `DXGI_PRESENT_ALLOW_TEARING` when the OS supports it.
4. Records per frame: index, scheduled time, the **QPC at `Present()` return**, the time spent inside the call, the injected extra ms. The QPC frequency is in the summary.
5. 30 warm-up presents through the same code path run before the origin and are not logged (they are real presents: an attached capture tool will see them).
6. Disposes the swap chain, device and window (also on error and on cancel).

### Why no timer-resolution change

The pacer does not touch the Windows timer resolution (a global side effect on every program). It measures how long `Sleep(1)` really takes on this PC (slowest of 20 samples plus 0.5 ms, clamped to 1-25 ms), sleeps in 1 ms steps only while the target is farther away than that margin, and **spins** for the rest. If a sleep overshoots the margin the margin grows (it never shrinks). On this PC the default timer tick is 15.6 ms, so the margin is about 17 ms and the harness effectively spins: accuracy is unaffected, **CPU cost is about one core** (100 % of one logical processor, reported in `cpu.percentOfOneCore`). Raising the thread priority was tried and made no measurable difference, so it is not used.

## Output

`--out FILE` writes the CSV; the summary goes next to it as `<name>.summary.json` (`--out` may not end in `.json`).

### CSV

The first 28 columns are exactly the PresentMon 2.6.0 columns, in order (checked against `tests/fixtures/presentmon/v2.6.0-real-sanitized.csv`), with PresentMon's formatting: comma separated, `.` decimals, CRLF, no BOM, literal `NA` for what the harness cannot truthfully provide. Five harness columns follow; a tolerant PresentMon parser ignores them (test plan row 3).

| Column | Value |
|---|---|
| `Application`, `ProcessID` | the harness executable name and process id |
| `SwapChainAddress` | the swap chain COM pointer (hex) |
| `PresentRuntime`, `SyncInterval`, `PresentFlags`, `AllowsTearing` | `DXGI`, `0`, `512` or `0`, `1` or `0` (what the harness passed / created) |
| `TimeInMs` | the harness's **own QPC at `Present()` return**, ms from the pacing origin, 4 decimals (PresentMon takes its value from ETW, so the two are not interchangeable) |
| `MsBetweenPresents` | difference of consecutive `TimeInMs`. Frame 0 has no earlier present: its value is measured from the pacing origin and equals its `TimeInMs`, so CSV row *k* is frame *k* |
| `MsInPresentAPI` | time spent inside the `Present()` call, measured around it |
| `PresentMode` | `NA`: which composition path the OS chose cannot be observed from inside the application |
| every other PresentMon column | `NA` |
| `JankscopeProvenance` | `Simulated` on every row |
| `JankscopeFrameIndex`, `JankscopeScheduledMs`, `JankscopeInjectedExtraMs`, `JankscopePresentQpcTicks` | the ground truth for that frame and the raw QPC ticks |

A detector under test must read frame times through the frame-log parser (which ignores the four ground-truth columns), and compare its findings with `groundTruth.hitches` from the summary.

### Summary JSON

`provenance`, `exportableAsMeasurement`, `notice`, `run` (parameters), `groundTruth.hitches`, `adapter` (`kind` hardware|warp, `provesPipelineOnly`, `note`, `fallbackReason`), `swapChain`, `timing` (QPC frequency, clock, timestamp point, `timerResolutionChange: "none"`, sleep margin, sleeps, spin iterations, late arrivals), `fidelity`, `cpu`, `environment` (`elevated`, OS, runtime; no machine or user name).

A WARP run adds `WARP software adapter: proves the pipeline only, not GPU timing.` to the notice, the adapter note and the console output.

### Fidelity numbers

`fidelity.presentTimeErrorMs`: |QPC at Present() return - scheduled time| per frame (max / median / p99, nearest-rank percentiles as in `docs/METRICS.md`). `fidelity.frameTimeErrorMs`: |achieved interval - ground-truth interval| per frame, which is what a frame-time detector is exposed to. `fidelity.hitches`: for each injected hitch, the achieved interval and the **carried extra** = achieved interval minus the same frame's ground-truth interval without the injection.

## Tests

`tests/Jankscope.Harness.Tests` (xUnit v2). Pure logic (schedule, pacer, loop, fidelity, CSV, summary, CLI) runs on a virtual clock on any machine. The integration tests carry `[Trait("requires", "d3d")]` and the `D3dFact` attribute, which creates a real window, device and swap chain at discovery time and **skips with the reason** when none can be created (hardware-only tests skip separately when only WARP exists).

```
dotnet test tests\Jankscope.Harness.Tests -c Release --filter "requires!=d3d"                       # no graphics needed
dotnet test tests\Jankscope.Harness.Tests -c Release --filter "requires=d3d" --logger "console;verbosity=detailed"   # prints the measured accuracy
```

## Measured accuracy (reference laptop, 2026-10-17, unelevated, hidden window, normal priority)

Pooled over 3000-frame runs with 8 hitches of 25 ms and 0.2 ms jitter; errors are absolute, in ms; the default Windows timer tick on this PC is 15.6 ms, so the sleep margin settled at 16.6-17.3 ms (the pacer spins).

| Adapter, rate (runs) | Present() return vs schedule: median / p99 / p99.9 / max | Frame time vs ground truth: median / p99 / max | Injected 25 ms read back as (error) | Harness CPU |
|---|---|---|---|---|
| hardware, 144 fps (5) | 0.063 / 0.223 / 0.572 / 2.235 | 0.013 / 0.187 / 2.139 | -0.07 ... +0.19 ms (40 hitches) | 101-103 % of one core |
| WARP, 144 fps (5) | 0.064 / 0.231 / 0.363 / 7.132 | 0.015 / 0.185 / 7.031 | -0.08 ... +0.06 ms (40) | 96-99 % |
| hardware, 240 fps (3) | 0.047 / 0.193 / 0.405 / 2.864 | 0.016 / 0.175 / 2.802 | -0.32 ... +0.13 ms (24) | 104-105 % |
| WARP, 240 fps (3) | 0.069 / 0.307 / 17.0 / 25.6 | 0.023 / 0.265 / 14.8 | -0.04 ... +0.21 ms (24) | 97-99 % |

The Present() call itself takes a median of 0.05-0.07 ms (p99 0.18-0.28 ms), and most of the error is that call: the timestamp is taken at its return.

**Rare stalls are real and are reported, not hidden.** In 48 000 frames there were a few single-frame stalls of 2-7 ms on both adapters and, in one of the three WARP 240 fps runs, a stall of up to 25 ms inside `Present()` (WARP's worker threads or the OS scheduler; nothing else was started by the harness). A stall that large is the size of a real hitch. The summary therefore lists every frame whose interval is off by more than 2 ms in `fidelity.disturbedFrames` (signed): an end-to-end test must treat such a frame as a harness disturbance, not as ground truth (for example re-run, or exclude the frame from precision accounting), and must not read a detector "false positive" there as a detector fault.

## Limits

- It is a ground truth for pacing, not a model of a game: the "work" is a colour clear, and a hitch is a late present, not slow GPU or CPU work.
- Accuracy is whatever the OS scheduler allows: a thread can be descheduled, or WARP starved, for milliseconds at any time (see above). Tests therefore assert median and p99 tightly, assert that disturbed frames are rare (at most 2 % of frames), and put no hard bound on the single worst frame.
- Hidden window: the swap chain presents to a window that is never shown. Presents return normally (`timing.occludedPresents` counts `DXGI_STATUS_OCCLUDED` results); a capture tool that depends on the compositor should use `--window-visible`.
- Hardware means the first non-software adapter, which on a hybrid laptop is whatever Windows lists first; the chosen adapter is in the summary.
