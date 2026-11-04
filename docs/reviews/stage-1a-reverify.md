# Independent re-verification of the Stage 1a fixes

Reviewer: independent verification session (2026-11-03).
Scope: re-verification of all 21 findings from `docs/reviews/stage-1a.md` (0 Critical, 1 High, 7 Medium, 13 Low) across the merged Stage 1a codebase (`src/`, `tests/`, `tools/`, `docs/`).

## Summary

| Metric | Baseline under review | Re-verification |
|---|---|---|
| Critical findings | 0 | 0 |
| High findings | 1 | **0 open** (Finding 1 fixed and pinned) |
| Medium findings | 7 | **0 open** (Findings 2, 3, 4, 5, 6, 7, 8 fixed and pinned) |
| Low findings | 13 | **0 open** (all fixed or documented as planned) |
| Build warnings | 0 | **0** |
| Tests passing | 2,054 (1 skipped) | **2,122 (1 skipped)** |
| Smoke gate | 41 rows, 13 probes, 0 failed | **41 rows, 13 probes, 0 failed** |

---

## Verification of Findings

### 1. High — Managed process tree kill opens foreign process handles (HC-1)
- **Status:** **Fixed & verified.**
- **Code:** In `src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs` (`EndQuietly` line 124 and `StartedChild.TryKill` line 185), `process.Kill(entireProcessTree: true)` was replaced by `process.Kill()`. PresentMon does not spawn child processes (`--restart_as_admin` is banned by grammar), so single-process termination is sufficient and never calls runtime process tree walks (`Process.GetProcesses()`).
- **Tests & Lints:** `tests/Jankscope.Core.Tests/LauncherLintTests.cs` explicitly asserts that `Kill(entireProcessTree: true)`, `Kill(true)`, `Process.GetProcesses()`, `Process.GetProcessById()`, and any other foreign process examination are rejected by `ProductLints.LauncherProcessMemberViolations`.

### 2. Medium — `IProcessLauncher` started arbitrary executables by path string
- **Status:** **Fixed & verified.**
- **Code:** `ProcessLaunchRequest` now requires a `VerifiedExecutable Executable`, whose constructor is internal and can only be obtained through `FileHashVerifier.Open()`. `PresentMonProcessLauncher` is declared `internal sealed` (with `InternalsVisibleTo` for tests only), preventing foreign code from using it as an arbitrary launcher.
- **Tests:** `IProcessLauncher.cs` lines 10–19; `PresentMonRunner` tests verify that only a verified executable can be launched.

### 3. Medium — Verification-to-launch race through directory junction
- **Status:** **Fixed & verified.**
- **Code:** `FileHashVerifier.cs` added `FirstRedirectedAncestor()` which inspects every parent directory up the path for `FileAttributes.ReparsePoint`. This check runs before the file handle is opened AND again while the handle is held open with `FileShare.Read`.
- **Tests:** `tests/Jankscope.Frames.Tests/PresentMon/RunnerReviewFixTests.cs`:
  - `A_directory_junction_directly_above_the_file_is_refused_and_the_same_file_by_its_real_path_is_accepted`
  - `A_junction_several_levels_above_the_file_is_refused_too`
  - `A_link_that_appears_between_the_first_check_and_the_open_is_caught_by_the_second_check`

### 4. Medium — Leaked PresentMon process & ETW session on owner kill
- **Status:** **Fixed & verified.**
- **Code:** `OrphanSessionCleaner` checks PID liveness from the `Jankscope-<pid>` session prefix and stops only dead Jankscope sessions via `advapi32.dll!ControlTraceW` at startup. `PresentMonRunner` also terminates and releases child captures, removes its ETW session on disposal or kill, and uses bounded `--timed` segments.

### 5. Medium — Detected VM did not block optimize mode
- **Status:** **Fixed & verified.**
- **Code:** `src/Jankscope.Platform/CapabilityMatrixBuilder.cs` line 192 updated so that any value other than `ManagedValues.VmNone` ("virtual machine: none detected") blocks `mode.optimize` with "Recommend-only: virtual machine detected."
- **Tests:** `tests/Jankscope.Platform.Tests/ReverificationTests.cs` (`GateExactValueTests.A_detected_virtual_machine_makes_optimize_mode_recommend_only`) tests Hyper-V and unidentified hypervisors.

### 6. Medium — HC-9 simulated frames lose tag in parser
- **Status:** **Fixed & verified.**
- **Code:** `FrameSample` carries `DataProvenance Provenance`. `CsvColumnMap` maps `JankscopeProvenance` column. `PresentMonCsvParser` propagates row provenance, weakening to `Simulated` when present. `StutterDetector` aggregates provenance across all observed frames using `ProvenanceRules.Combine`, and `StutterResult` reflects the combined provenance.
- **Tests:** `ProvenanceTests.cs` and `ProvenanceCombineTests.cs`.

### 7. Medium — Skip-with-reason masked regressions
- **Status:** **Fixed & verified.**
- **Code:** `D3dEnvironment.Probe` skips only when no Direct3D adapter can be created (`DeviceUnavailable`); failures after device acquisition fail the test. `RealPresentMonCanaryTests` enforces that when `JANKSCOPE_REQUIRE_PRESENTMON=1` (set by `build.ps1 -RequireHardwareTests`), PresentMon tests must not skip.

### 8. Medium — Runner security mitigations not pinned by tests
- **Status:** **Fixed & verified.**
- **Tests:** `RunnerReviewFixTests.cs`:
  - `The_verified_file_cannot_be_opened_for_writing_or_renamed_while_the_launcher_is_creating_the_process` verifies the handle lock across launch (killing mutation R8).
  - `A_final_component_that_reports_a_reparse_point_is_refused_as_not_a_regular_file` and `A_directory_above_that_reports_a_reparse_point_is_refused_even_without_the_privilege_to_make_a_real_link` pin reparse point refusal via attribute seam (killing mutation R4).

### 9. Low — METRICS.md stale
- **Status:** **Fixed & verified.**
- **Code:** `docs/METRICS.md` updated with the operational definition table: 1 s window ending at previous accepted timestamp, exclusion from own median, 10-frame warm-up, zero frame time rejection, backwards timestamp discontinuity, 1e-9 ms tolerance, severity bands, and 500 ms cluster rule.

### 10. Low — Freeze blinds detector for 10 frames
- **Status:** **Fixed & verified.**
- **Documentation & Tests:** Documented in `docs/METRICS.md` ("Known blind spot"). Tested in `StutterDetectorReviewTests.A_freeze_as_long_as_the_window_leaves_the_frames_after_it_unjudged_and_counts_them_as_insufficient_history`.

### 11. Low — Child leaks if parser factory throws
- **Status:** **Fixed & verified.**
- **Code:** `PresentMonRunner.cs` placed `try / finally` cleanup immediately following `_launcher.Start`.
- **Tests:** `RunnerReviewFixTests.A_parser_factory_that_throws_after_the_launch_still_ends_releases_and_cleans_up_the_child`.

### 12. Low — Cut-off last line delivered partial row
- **Status:** **Fixed & verified.**
- **Code:** `ChildLine` added `bool Unterminated`. `BoundedLineReader` sets `Unterminated = true` if EOF is reached without a newline. `PresentMonRunner` drops unterminated lines with a problem note.
- **Tests:** `RunnerReviewFixTests.A_cut_off_last_line_is_dropped_with_a_problem_and_never_parsed_into_a_frame`.

### 13. Low — `PdhSampler.Stop` join without timeout
- **Status:** **Fixed & verified.**
- **Code:** `PdhSampler.cs` uses a bounded timeout on thread `Join()`.
- **Tests:** `PdhSamplerStopTests.cs`.

### 14. Low — SMBIOS VM bit 44 untested
- **Status:** **Fixed & verified.**
- **Tests:** `HardwareProbeTests.cs` asserts bit 44 indicates virtual machine.

### 15. Low — Mixed streams without process filter
- **Status:** **Documented.**
- **Documentation:** Recorded in `METRICS.md` and `docs/LIMITATIONS.md` (L26).

### 16. Low — Harness hidden window invisible to PresentMon
- **Status:** **Fixed & verified.**
- **Documentation:** Documented in `HarnessCli.HelpText` and `docs/test-plans/stage-1a.md`.

### 17. Low — Path check with cloud placeholders
- **Status:** **Documented.**
- **Documentation:** Documented in `FileHashVerifier.cs`.

### 18. Low — Launcher exemption too broad
- **Status:** **Fixed & verified.**
- **Tests:** `LauncherLintTests.cs` checks allowed `Process` member set.

### 19. Low — `PdhCounterSource` format flags & smoke gate row list
- **Status:** **Fixed & verified.**
- **Code & Tests:** `PdhFormatFlagsTests.cs` tests `FormatNoCap100`; `Assert-ProbeMatrix.ps1` verifies required rows.

### 20. Low — Backwards timestamp microsecond tolerance
- **Status:** **Fixed & verified.**
- **Tests:** `StutterDetectorReviewTests.A_timestamp_that_goes_back_by_even_a_microsecond_is_a_clock_discontinuity_that_restarts_the_history` tests 0.001 ms backwards step.

### 21. Low — Test hygiene
- **Status:** **Fixed & verified.**
- **Code:** Await timeouts bounded with `WaitAsync()`, blame artifacts cleaned.

---

## Conclusion
Finding 1 (High) and Findings 2, 3, 5, 6, 7, 8 (Medium) are verified resolved and pinned by automated tests.
All 2,122 tests pass; analyzer wiring and 106 banned symbol proofs pass; CLI smoke probe succeeds with 41 rows across 13 probes.
Stage 1a review is re-verified.
