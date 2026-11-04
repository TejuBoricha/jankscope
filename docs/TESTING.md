# Testing, Static Proofs & Review Strategy

Specification: [`MASTER_PROMPT_v4.md`](../MASTER_PROMPT_v4.md) §18, plus the standing engineering gate: **automated static proofs, comprehensive tests, and independent code review at every stage**.

---

## 1. Run Everything in One Command

```powershell
./build.ps1                         # Restore, Build (Release, 0 warnings), Static Proofs, 2,505+ Tests, Smoke Gate
./build.ps1 -Clean                  # Full clean verification from scratch
./build.ps1 -RequireHardwareTests   # Reference bench mode: any skipped D3D11 or PresentMon test fails the build
```

---

## 2. Verification Layers (`2,505+` Automated Tests + 3 Build-Time Proofs)

| Layer | Location | Count | What It Proves |
| :--- | :--- | :--- | :--- |
| **Banned-Symbol Coverage Proof** | [`tools/dev/verify-banned-symbols.ps1`](../tools/dev/verify-banned-symbols.ps1) | 106 rules | Compiles a synthetic probe project against [`BannedSymbols.txt`](../BannedSymbols.txt) and proves every single `RS0030` entry actually fires. |
| **Analyzer Wiring Proof** | [`tools/dev/verify-analyzer-wiring.ps1`](../tools/dev/verify-analyzer-wiring.ps1) | 7 projects | Queries MSBuild directly (`dotnet build -getItem:Analyzer`) across all 7 product/tool projects to prove `BannedApiAnalyzers` and `BannedSymbols.txt` cannot be silently dropped. |
| **Core & Repository Lints** | [`tests/Jankscope.Core.Tests`](../tests/Jankscope.Core.Tests) | 766 tests | Metric math, provenance lattice (`HC-9`), `AntiCheatSafety` decision table (`HC-1`), ETW session classification, catalog schema validation, two-layer redaction (`Redactor` + `ExportGuard`), and repository-wide static safety lints (`RepoLintTests`, `LauncherLintTests`). |
| **Platform & Hardware Probes** | [`tests/Jankscope.Platform.Tests`](../tests/Jankscope.Platform.Tests) | 344 tests | Probe timeout/exception isolation (`TaskCreationOptions.LongRunning`), CPU topology, GPU LUID discovery, CCD display topology, PDH counter sampling, `OrphanSessionCleaner`, and `HotkeyRegistrar`. |
| **Frames, Runner & Incidents** | [`tests/Jankscope.Frames.Tests`](../tests/Jankscope.Frames.Tests) | 1,098 tests | Lock-free `RingBuffer<T>` concurrency/zero-allocation proofs, `StutterDetector` equivalence against `NaiveStutterOracle`, 1M-row streaming `PresentMonCsvParser` bounded-heap check, `FileHashVerifier` symlink/junction rejection, `PresentMonRunner` watchdog/supervision, `CaptureClockMap`, `CandidateAnchorFinder`, and `IncidentRecorder`. |
| **Direct3D 11 Synthetic Harness** | [`tests/Jankscope.Harness.Tests`](../tests/Jankscope.Harness.Tests) | 266 tests | Deterministic `SplitMix64` schedule generation, QPC frame pacer, Direct3D 11 flip-model presenter (hardware GPU + software `Microsoft Basic Render Driver` / WARP fallback), and 100% precision/recall exit tests against `StutterDetector`. |
| **WPF Desktop App & Live E2E** | [`tests/Jankscope.App.Tests`](../tests/Jankscope.App.Tests) | 32 tests | `CaptureController`, `AppSettings` atomic persistence, `TimelineView` pixel-bucket spike preservation, STA WPF window smoke tests, and live PresentMon + visible D3D11 harness end-to-end mark/anchor/export verification. |
| **CLI Smoke Gate & Self-Test** | [`tools/dev/test-smoke-gate.ps1`](../tools/dev/test-smoke-gate.ps1) | 41 rows / 14 probes | Proves `Assert-ProbeMatrix.ps1` rejects malformed/failed matrices and then verifies live `jankscope.exe probe --json` output. |
| **Observer-Effect Benchmark** | [`tools/dev/Measure-Overhead.ps1`](../tools/dev/Measure-Overhead.ps1) | 10-run A/B | Paired interleaved A-B-A-B benchmark measuring PresentMon + `jankscope-app` CPU, Working Set, present-error delta, and zero network connections ([`docs/benchmarks/overhead-v1.md`](benchmarks/overhead-v1.md)). |

---

## 3. Mutation Proofs & Code Review Gate

1. **Safety Tests Must Be Proven Able to Fail:** Every safety-critical lint, analyzer rule, or redaction guard is verified with planted violations (recorded in [`docs/reviews/`](reviews/)).
2. **Independent Stage Reviews:** Recorded in [`docs/reviews/stage-0.md`](reviews/stage-0.md), [`docs/reviews/stage-0-pass2.md`](reviews/stage-0-pass2.md), [`docs/reviews/stage-1a.md`](reviews/stage-1a.md), and [`docs/reviews/stage-1b.md`](reviews/stage-1b.md). No stage closes with an open Critical or High finding.
