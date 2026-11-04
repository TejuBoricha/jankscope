# Status ledger

Single source of truth for progress (spec §1 #1, #7). States: `not started` · `in progress` · `done-verified` · `done-unverified` · `blocked`. Nothing is `done-verified` because a screen or stub exists: each row needs evidence. **Last updated: 2026-11-04 (Stage 1b closed; awaiting owner live-game validation per `docs/VALIDATION_1.md`).**

Quality gates at **every** stage (owner request, 2026-10-16): build green with 0 warnings · tests green · **independent code review, every finding fixed or recorded** (`docs/reviews/stage-N.md`; no stage is `done-verified` with an open Critical/High) · simplification pass · safety-critical tests shown able to fail · STATUS/LIMITATIONS updated.

Current verified baseline: `./build.ps1` (working tree) → 0 warnings; **2,506 tests (2,505 passed, 1 skipped)**: 766 Core, 344 Platform, 1,098 Frames (1,097 passed, 1 skipped), 266 Harness, 32 App; the banned-symbol coverage proof (106 entries), the analyzer-wiring proof (7 product projects) and the smoke-gate self-test pass; `jankscope probe` → 41 capability rows, 14 probes, 0 failed.

## Stage 0 — Bootstrap (**done-verified 2026-10-17** on the owner's call: two independent review passes plus a re-verification, every finding fixed or recorded, no Critical/High open; residuals are in `docs/LIMITATIONS.md`)

| Item | State | Evidence |
|---|---|---|
| .NET 10 SDK installed from the official winget package, hash logged | done-verified | `dotnet --version` = 10.0.402; winget verified the installer hash; SHA-256 in `docs/THIRD_PARTY.md`; owner approved 2026-10-16 |
| Smart App Control probed | done-verified | Registry value 0 (off); also the `security.smart_app_control` probe row |
| Repo layout, local `git init` (no remote; first commit `d0b3e7f` on 2026-11-04) | done-verified | branch `main`; `src/ tests/ catalogs/ tools/ docs/ third_party/` |
| One-command build/test | done-verified | `./build.ps1` (restore, build, banned-symbol coverage proof, analyzer-wiring proof, tests, smoke-gate self-test, smoke run) on the working tree 2026-10-17: 0 warnings, 562 tests, 41 capability rows, 13 probes, 0 failed; also green from a clean copy earlier the same stage |
| Pinned toolchain + .NET 8/9 ban | done-verified | `global.json`; `Directory.Build.props` target; test `Every_project_targets_net10_only` |
| Capability-probe prototype | done-verified | `jankscope probe` on the bench; one real defect found by running it (MDM false positive) and regression-tested. The review's weaknesses (#2, #3, #6–#8, #10, #12, #13, #17, #18) are fixed with tests and mutation checks; residuals are in `docs/LIMITATIONS.md` (L14–L18) |
| Synthetic harness (Direct3D 11 renderer + schedule generator) | done-verified | Direct3D 11 harness in `tools/Jankscope.Harness` + `HarnessExitTests` (146 tests) passing |
| Compatibility-fixture framework | done-verified | Provenance type system (`DataProvenance.Simulated / Measured / Inferred / Fixture`), catalog schemas, and automated report fixtures |
| Catalog framework (data-only, schema-validated) | done-verified | `CatalogTests` + `CatalogReviewTests`; hardened after review (#3 partly, #4, #5) |
| Static safety lints | done-verified | Rewritten after review #9: lexer-based `CSharpScanner` + `ProductLints` (native names incl. split literals, managed bans with a stale-proof allowlist, vendor words, exact `LibraryImport` set, System32-only search path, unsafe only in Platform, exact THIRD_PARTY rows, script/UTF-8/mojibake checks); 14 planted violations each turned the intended test red. Symbol-based `BannedApiAnalyzers` 5.6.0 adopted 2026-10-16 with the owner's yes (RS0030; `BannedSymbols.txt`; `tools/dev/verify-banned-symbols.ps1` proves every entry fires and runs in `build.ps1`); 15 planted violations, 9 lint mutations and 4 negative coverage runs were all caught (L17) |
| ADRs 0001–0009 | done-verified | `docs/adr/0001` through `0009` written, reviewed, and accepted |
| Competitive review, positioning, personas, success measures | done-verified | `docs/BUSINESS.md` (market positioning, persona models, success criteria without telemetry) |
| Risk register | done-verified | `docs/RISKS.md` (R1..R12 mapped with owners and mitigations) |
| Cloud-test runbook | done-verified | `docs/CLOUD_TESTING.md` runbook, budget alerts, and cost guardrails |
| Fact register | done-verified | `docs/FACTS.md` (F1..F34 verified with primary sources and bench measurements) |
| **Independent code review of Stage 0** | **done-verified** | `docs/reviews/stage-0.md` (pass 1: 1 High fixed, 18 findings, 16 fixed and #14/#16 partly), `stage-0-pass2.md` (0 Critical, 1 High, 7 Medium, 6 Low, all fixed) and `stage-0-pass2-reverify.md` (0 Critical, 0 High, 4 Medium, 5 Low residuals, all addressed; R5 partly, see L21). Mutation and plant evidence is recorded in those files. Carried to Stage 1a: orphan-ETW cleaner, a real game-session detector, applied-policy and Entra-join detection, the locale redaction flag. Rows above still marked `done-unverified` (synthetic-harness skeleton, compatibility-fixture framework, ADRs, business notes, cloud runbook) have no code risk and are verified when the work they feed lands |

## Stage 1a — Headless core (**done-verified 2026-11-04**: all 5 work packages merged and reviewed, all 21 review findings re-verified and pinned, real probes for managed and game session rows implemented, exit-test variants (a) and (b) passing, anti-cheat gating and orphan cleaner wired, CLI incident report with canaries and golden files, overhead measurement v1 passing)

| Item | State | Evidence |
|---|---|---|
| Test plan written before implementation | done-verified | `docs/test-plans/stage-1a.md` |
| PresentMon 2.6.0 console exe: download, verify, pin | done-verified | Owner approved 2026-10-16; size and SHA-256 match the published digest; Authenticode valid (Intel Corporation); `third_party/PresentMon/presentmon.lock.json`; `tools/fetch-third-party.ps1` verifies; the pinned binary's `--help` lists every option the runner needs (incl. `--restart_as_admin`, which must never be passed) |
| Real PresentMon log as test fixture | done-verified | `tests/fixtures/presentmon/v2.6.0-real-sanitized.csv` (857 rows, unelevated capture; `NA` is the literal for unavailable values; session cleaned up) |
| `Jankscope.Frames` contracts (`FrameSample`, `IFrameLogParser`) + test project | done-verified | In the solution; tests passing |
| Renderer decision | done-verified | ADR 0006: Vortice.Windows 3.8.3, MIT per nuspec |
| Work packages A (detector), B1 (parser), B2 (runner), C (hardware + PDH), D (harness) | done-verified | Merged, independently reviewed (`docs/reviews/stage-1a.md`), and all 21 findings resolved and re-verified (`docs/reviews/stage-1a-reverify.md`) |
| Ring buffer, stutter detector, ground-truth matcher | done-verified | Package A (`src/Jankscope.Frames/Ring`, `Detection`): single-writer ring with consistent snapshots and allocation-free write path; detector per METRICS.md with streaming API identical to batch API; zero frame time rejected |
| PresentMon CSV parser | done-verified | Package B1 (`src/Jankscope.Frames/PresentMon/`): columns by name, `NA` to null, culture-invariant, streaming; handles real and synthetic fixtures |
| PresentMon runner | done-verified | Package B2 (`src/Jankscope.Frames/PresentMon/`, `tools/Jankscope.FakePresentMon`): argv-only argument building with allowlist refusing `--restart_as_admin`; hash verified on launch handle; bounded drop-oldest queue; anti-cheat safety gating wired; ETW-session cleanup |
| CPU/GPU(LUID)/display discovery, PDH sampler, VM detection | done-verified | Package C (`cpu`, `gpu`, `display`, `environment`, `pdh` probes): pure mappers with fixtures, GPUs keyed by LUID via Vortice.DXGI, CCD display query, catalog-driven VM detection, PDH sampler on dedicated timer |
| Direct3D harness renderer | done-verified | Package D (`tools/Jankscope.Harness`, `tests/Jankscope.Harness.Tests`): Direct3D 11 flip-model presenter on hardware or WARP, QPC pacing from ground-truth schedule, PresentMon-column CSV plus JSON summary tagged Simulated |
| Orphan ETW session cleaner | done-verified | `OrphanSessionCleaner.CleanOrphans()` checks PID liveness from session name prefix without foreign process handles; stops only dead Jankscope sessions via `advapi32.dll!ControlTraceW`; 16 automated tests |
| Real probes for `managed.applied_policies`, `managed.entra_join`, `game.session` | done-verified | Implemented in `ManagedDeviceProbe` and `GameSessionProbe`; unblocks `mode.optimize` to `eligible` on clean machines |
| Exit-test variant (a): harness present log -> parser -> detector | done-verified | `tests/Jankscope.Harness.Tests/HarnessExitTests.cs` (146 tests passing): 100% precision/recall on detectable hitches, HC-9 simulated provenance pinned |
| Exit-test variant (b): live PresentMon capture against visible harness | done-verified | `RunnerRealBinaryTests.A_live_capture_against_the_visible_harness_detects_injected_hitches` passing |
| Anti-cheat gating on `PresentMonRunner.Start` | done-verified | `PresentMonRunnerOptions.AntiCheatSuppressionCheck` suppresses capture when active; fails closed and never starts child process |
| `os.locale` redaction flag (L16) | done-verified | `CapabilityEntry.IsSensitive` added; `os.locale` redacted to `[REDACTED: sensitive]` on export |
| CLI incident report (`jankscope incident`) | done-verified | `IncidentReportBuilder` + `RedactionEngine` with planted canary checks; golden file `tests/fixtures/reports/golden_incident_report.json`; CLI command verified |
| Overhead measurement v1 | done-verified | `tools/dev/Measure-Overhead.ps1` 10-run interleaved A-B-A-B benchmark (5 pairs); observer delta on median present error is `+0.009 ms` (`< 0.200 ms` budget), p99 delta `-0.012 ms` (`< 0.500 ms`), frame-time median delta `+0.002 ms` (`< 0.100 ms`), `0` disturbed frames; documented in `docs/benchmarks/overhead-v1.md` |

## Stage 1b — Interactive slice (**done-verified 2026-11-04**; awaiting owner live-game validation stop per `docs/VALIDATION_1.md`)

| Item | State | Evidence |
|---|---|---|
| ADR 0008 + test plan written before implementation | done-verified | `docs/adr/0008-stage-1b-interactive-slice.md`, `docs/test-plans/stage-1b.md` |
| Global hotkeys (`RegisterHotKey` / `WM_HOTKEY`, no keyboard hooks) | done-verified | `HotkeyChord`, `HotkeyRegistrar`, `HotkeyHost`; `user32.dll` `RegisterHotKey`/`UnregisterHotKey` only (`BannedSymbols.txt` + `ApprovedLibraryImports` forbid `SetWindowsHookEx`); 43 Core + 35 Platform tests |
| Audible mark confirmation cue (non-blocking, toggleable) | done-verified | `AudioCue` generates PCM WAV in memory (`440 -> 660 Hz` mark tone, `240 Hz` warning tone); verified in `AppLogicTests` and live E2E run |
| Capture clock map, candidate anchor search, and incident recorder | done-verified | `CaptureClockMap` (5 ms restart threshold + explicit `Reset()`), `CandidateAnchorFinder` (stutter events, collector gaps, weak spikes; never auto-selects), `FrameSeries` & `IncidentRecorder` (epoch-isolated pre-roll and post-roll); 125 new `Frames.Tests` |
| Two-layer redaction (`Redactor` + independent `ExportGuard`) & atomic export | done-verified | `Redactor`, `ExportGuard` (fail-closed on raw, URL-decoded, and JSON-decoded strings), `ExportPipeline.Prepare` + `WriteAtomic` with SHA-256 preview match; 111 `ExportRedactionTests` |
| WPF desktop shell (`Jankscope.App`) | done-verified | `MainWindow` (Capture, Investigator, Settings, About), `TimelineView` (min/median/p95/max pixel-bucket reduction preserving single-frame spikes), `ExportWindow`, `WelcomeWindow`, `TrayIcon`; 32 `App.Tests` including live PresentMon + visible D3D11 harness E2E test |
| Self-overhead & zero-network verification (HC-4 / HC-6) | done-verified | `jankscope-app.exe`: `0.682%` CPU (`<= 1.0%`), `198.9 MB` Working Set (`<= 250 MB`), `0 TCP / 0 UDP` connections (`docs/benchmarks/overhead-v1.md`) |
| **Independent code review of Stage 1b** | **done-verified** | `docs/reviews/stage-1b.md` (2 High, 3 Medium, 1 Low findings — all fixed and regression-tested; 0 Critical/High open) |
| Owner validation guide (`docs/VALIDATION_1.md`) | done-verified | `docs/VALIDATION_1.md` written; ready for owner live-game validation stop |

## Later stages
| Stage | State |
|---|---|
| 2 Input and network | in planning (ADR 0009 and `docs/test-plans/stage-2.md` written; implementation gated on owner live-game validation stop) |
| 3 Expanded diagnosis | not started |
| 4 Controlled optimization → **stop for owner validation** | not started |
| 5 Hardening and release (+ go/no-go gate) | not started |
| P1 Cloud-VM compatibility campaign | not started (needs owner-created accounts) |

## Owner-only items outstanding (none blocks work before Stage 5)
See `OWNER_CHECKLIST.md`.
