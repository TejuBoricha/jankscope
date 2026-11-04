# Independent Review: Stage 1b (Interactive Slice)

**Date:** 2026-11-04
**Scope:** Stage 1b — Global hotkeys (`HotkeyChord`, `HotkeyRegistrar`), audible cue (`AudioCue`), capture session & clock mapping (`CaptureClockMap`, `CaptureSession`, `PresentingProgramScanner`), candidate anchor search & incident recorder (`CandidateAnchorFinder`, `FrameSeries`, `IncidentRecorder`, `TimelineBuckets`, `IncidentBundle`), two-layer redaction & export guard (`RedactionContext`, `Redactor`, `ExportGuard`, `ExportPipeline`), and the WPF desktop shell (`Jankscope.App`: `AppHost`, `CaptureController`, `MainWindow`, `TimelineView`, `ExportWindow`, `WelcomeWindow`, `TrayIcon`).

---

## 1. Review Methodology

Per `MASTER_PROMPT_v4.md` section 17, Stage 1b was reviewed from first principles against the code and live execution rather than trusting handoff notes or status tables:
1. **Static & Architectural Audit:** Read every Stage 1b production and test file across `Jankscope.Core`, `Jankscope.Platform`, `Jankscope.Frames`, and `Jankscope.App`, plus `BannedSymbols.txt`, `RepoLintTests.cs`, and `ProductLints.cs`.
2. **Adversarial Edge-Case & Mutation Probing:** Tested capture restarts across shared ring buffers, post-roll splicing across epoch boundaries, concurrent `Start()` calls during `Stopping`, partial-match redaction ordering (`ProfilePathRegex` / `UncHostRegex` vs `_known` substring replacements, IPv4-mapped IPv6 addresses, zone indices), and `NaN` / non-positive frame times in `IncidentReportBuilder`.
3. **Live Hardware & End-to-End Verification:** Executed the full build, banned-symbol proof, analyzer-wiring proof, smoke gate, real PresentMon + visible D3D11 harness end-to-end mark/anchor/export integration test, 10-run interleaved A-B-A-B observer-effect benchmark (`Measure-Overhead.ps1`), and runtime process inspection (`Get-NetTCPConnection`, working set, CPU %).

---

## 2. Findings & Resolutions

### Finding 1 (High) — `FrameRingExtensions.SelectWindow` and `FrameSeries.ContinueWith` Mixed Capture Epochs Across Restarts
- **Location:** `src/Jankscope.Frames/Ring/FrameRingExtensions.cs`, `src/Jankscope.Frames/Incidents/FrameSeries.cs`, `src/Jankscope.Frames/Incidents/IncidentRecorder.cs`
- **Issue:**
  - `FrameRingExtensions.SelectWindow` documented that a clock jumping backwards inside the span cuts the window there instead of reaching into an unrelated epoch, but the loop condition `chronological[first - 1].TimestampMs >= startTimestampMs` only compared each sample against `startTimestampMs = endTimestampMs - windowMs`. When PresentMon restarted (or a new capture session started on the shared `History` ring), timestamps reset near `0`. Any mark within the first 120 seconds of Run 2 had `startTimestampMs < 0`, causing `SelectWindow` to walk across the backwards step (`Run 1 end -> 0 ms`) and include Run 1's frames in Run 2's pre-roll and `CandidateAnchorFinder` search.
  - `FrameSeries.ContinueWith(later, endMs)` iterated from index `0` of `later` checking `sample.TimestampMs > lastMs && sample.TimestampMs <= endMs`, which could splice frames from an earlier or restarted run into a mark's post-roll, and `IncidentRecorder.CollectWindow` checked `snapshot[^1].TimestampMs >= end` without verifying that `snapshot[^1]` belonged to the same continuous run as `mark.PreRoll`.
- **Resolution:**
  - Updated `FrameRingExtensions.SelectWindow` to first isolate the latest monotonic, single-application run at the tail of `chronological` (`SameCaptureEpoch`) before selecting `[startTimestampMs, endTimestampMs]`.
  - Updated `FrameSeries.ContinueWith(later, endMs, out bool reachedEnd)` to locate the end of `PreRoll` from the back of `later`, walk forward only while timestamps are non-decreasing and the application is unchanged, and report `reachedEnd` only when that same continuous run reaches `endMs`.
  - Added regression tests `SelectWindow_cuts_at_a_clock_restart_or_application_change_instead_of_mixing_epochs` (`FrameWindowTests.cs`) and `A_capture_that_restarts_isolates_the_new_pre_roll_candidates_and_the_old_post_roll` (`IncidentRecorderTests.cs`).

### Finding 2 (High) — Redaction Ordering & Partial-Segment Leaks in `Redactor` and `ExportGuard`
- **Location:** `src/Jankscope.Core/Reports/Redactor.cs`, `src/Jankscope.Core/Reports/ExportGuard.cs`
- **Issue:**
  - In `Redactor.Redact`, `_known` value replacements ran *before* `ProfilePathRegex`, `OneDriveRegex`, and `UncHostRegex`. When a profile folder or UNC host name contained a known value as a prefix (e.g., `C:\Users\Zanzibar_Quinn_000\Desktop` or `\\Zanzibar_Quinn-pc\share`), `_known` replaced the prefix with `<USER>`, leaving `C:\Users\<USER>_000\Desktop` and `\\<USER>-pc\share`. Because `ProfilePathRegex` excluded `<` and `>` and `ExportGuard.ForeignProfilePath` used `(?!<USER>)[^<>]+`, neither the path regex nor `ExportGuard` caught the remaining `_000` or `-pc` suffix.
  - `Redactor.Ipv6Regex` ran before `Ipv4Regex` without matching embedded IPv4 tails or `%zone` suffixes. On IPv4-mapped IPv6 addresses such as `::ffff:192.168.1.5`, `Ipv6Regex` matched `::ffff:192` and left `.168.1.5` unredacted; on `fe80::1%eth0`, `%eth0` was left behind.
- **Resolution:**
  - Reordered `Redactor.Redact` so `ProfilePathRegex`, `OneDriveRegex`, and `UncHostRegex` replace whole path/UNC/OneDrive segments before `_known` substring replacements run.
  - Extended `Redactor.Ipv6Regex` to match embedded IPv4 dotted-quad tails (`::ffff:192.168.1.5`, `64:ff9b::192.0.2.128`) and optional `%zone` indices (`%eth0`, `%12`).
  - Tightened `ExportGuard.ForeignProfilePath` (`(?!<USER>(?:[\\/""'\s]|$))`) and `ExportGuard.ForeignUncHost` (`(?!<HOST>\\)`) so partial tokens inside a profile folder or UNC host are blocked by `ExportGuard.Verify`.
  - Added regression test `Profile_paths_unc_hosts_and_ipv4_mapped_ipv6_redact_completely_without_partial_suffix_leaks` (`ExportRedactionTests.cs`).

### Finding 3 (Medium) — `CaptureController.Start` Allowed Overlapping Session While Previous Session Was `Stopping`
- **Location:** `src/Jankscope.App/Services/CaptureController.cs`
- **Issue:**
  - `CaptureStatus.IsActive` excludes `CaptureState.Stopping`. Calling `Start()` while the prior `CaptureSession` was still in `Stopping` (or before `_session.Completion.IsCompleted`) allowed a second `CaptureSession` to start writing to the shared single-writer `History` ring buffer concurrently, and did not reset `Clock` (`CaptureClockMap`) between sessions.
- **Resolution:**
  - Updated `CaptureController.Start` to refuse starting while `_session` is active, `Stopping`, or `!_session.Completion.IsCompleted`, and to call `Clock.Reset()` before starting a new session.
  - Added regression test `Start_refuses_while_previous_session_is_still_stopping` (`AppLogicTests.cs`).

### Finding 4 (Medium) — `CaptureClockMap` Sub-Second Restart Threshold & Explicit Run Reset
- **Location:** `src/Jankscope.Frames/Incidents/CaptureClockMap.cs`, `src/Jankscope.Frames/Capture/CaptureSession.cs`
- **Issue:**
  - `CaptureClockMap.RestartThresholdMs` was `1000.0` ms, so a capture run that lasted less than 1 second before restarting near `0 ms` did not advance the epoch or clear stale delay observations.
- **Resolution:**
  - Lowered `RestartThresholdMs` to `5.0` ms, added `CaptureClockMap.Reset()`, and updated `CaptureSession.PumpAsync` to call `_clock.Reset()` whenever `captured.Run` changes.
  - Added regression test `A_short_run_restart_or_explicit_reset_starts_a_new_epoch` (`CaptureClockMapTests.cs`).

### Finding 5 (Medium) — `IncidentReportBuilder` Unfiltered Frame Times & Zero Coalescing
- **Location:** `src/Jankscope.Core/Reports/IncidentReport.cs`, `src/Jankscope.Frames/Reports/IncidentReportBuilder.cs`
- **Issue:**
  - `IncidentReportBuilder.Build` passed raw `FrameTimeMs` values to `FrameStats` without filtering non-finite/non-positive values, and coalesced `NaN` statistics to `0` (`double.IsFinite(avgFps) ? ... : 0`), violating the rule that unknown/uncomputable values must be `null` rather than fabricated zeros.
- **Resolution:**
  - Filtered `samples` to finite positive frame times before calling `FrameStats`, and changed `AverageFps`, `MedianFrameTimeMs`, `P99FrameTimeMs`, and `P999FrameTimeMs` on `MetricSummary` to `double?` (`null` when uncomputable).
  - Added regression test `Incident_report_filters_unusable_frame_times_and_uses_null_instead_of_fabricated_zero` (`IncidentReportTests.cs`).

### Finding 6 (Low) — `Measure-Overhead.ps1` Hardcoded Pass Status & Pipeline Output Pollution
- **Location:** `tools/dev/Measure-Overhead.ps1`
- **Issue:**
  - `Measure-Overhead.ps1` ran only 2 A/B pairs, hardcoded `**PASS**` in its markdown table, and did not suppress stdout from `jankscope-harness.exe` inside `Run-Condition`.
- **Resolution:**
  - Updated `Measure-Overhead.ps1` to run 5 interleaved A/B pairs (10 runs) by default, compute median and IQR for Conditions A and B, evaluate pass/fail dynamically against the section 14 limits, throw on any threshold failure, and write clean UTF-8 without BOM.

---

## 3. Hard-Constraint & Verification Summary

| Check | Result | Evidence |
| :--- | :--- | :--- |
| **Build (`./build.ps1`)** | **PASS** (0 warnings, 0 errors) | 12 projects compiled under `.NET SDK 10.0.402` with `TreatWarningsAsErrors=true` |
| **Banned Symbols (`BannedSymbols.txt`)** | **PASS** | 106 banned entries, 109 proof lines flagged, 11 allowed controls clean; wired across all 7 product projects |
| **Test Suite** | **PASS** (`2,504 passed, 0 failed, 1 skipped` / `2,505 total`) | `Core.Tests`: 765, `Frames.Tests`: 1,097 (+1 symlink skip when unprivileged), `Platform.Tests`: 344, `Harness.Tests`: 266, `App.Tests`: 32 |
| **Real PresentMon + Visible D3D11 Harness E2E** | **PASS** | `Real_presentmon_and_visible_harness_end_to_end_mark_anchor_and_redacted_export` in `AppLogicTests.cs` |
| **10-Run Interleaved A/B Overhead (`Measure-Overhead.ps1`)** | **PASS** | Present() median delta `+0.009 ms` (`< 0.200 ms`), p99 delta `-0.012 ms` (`< 0.500 ms`), FrameTime median delta `+0.002 ms` (`< 0.100 ms`), `0` disturbed frames (`docs/benchmarks/overhead-v1.md`) |
| **Desktop App Self-Overhead & Zero Network (HC-4 / HC-6)** | **PASS** | `jankscope-app.exe`: `0.682%` CPU (`<= 1.0%`), `198.9 MB` Working Set (`<= 250 MB`), `0 TCP / 0 UDP` connections |
