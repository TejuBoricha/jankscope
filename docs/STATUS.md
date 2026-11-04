# Status ledger

Single source of truth for progress (spec §1 #1, #7). States: `not started` · `in progress` · `done-verified` · `done-unverified` · `blocked`. Nothing is `done-verified` because a screen or stub exists: each row needs evidence. **Last updated: 2026-11-04 (Stage 1a closed; see `HANDOFF.md`).**

Quality gates at **every** stage (owner request, 2026-10-16): build green with 0 warnings · tests green · **independent code review, every finding fixed or recorded** (`docs/reviews/stage-N.md`; no stage is `done-verified` with an open Critical/High) · simplification pass · safety-critical tests shown able to fail · STATUS/LIMITATIONS updated.

Current verified baseline: `./build.ps1` (working tree and clean copy) → 0 warnings; 2054 tests (654 Core, 309 Platform, 973 Frames, 118 Harness; 1 skipped); the banned-symbol coverage proof (106 entries), the analyzer-wiring proof and the smoke-gate self-test pass pass; `jankscope probe` → 41 capability rows, 13 probes, 0 failed.

## Stage 0 — Bootstrap (**done-verified 2026-10-17** on the owner's call: two independent review passes plus a re-verification, every finding fixed or recorded, no Critical/High open; residuals are in `docs/LIMITATIONS.md`)

| Item | State | Evidence |
|---|---|---|
| .NET 10 SDK installed from the official winget package, hash logged | done-verified | `dotnet --version` = 10.0.402; winget verified the installer hash; SHA-256 in `docs/THIRD_PARTY.md`; owner approved 2026-10-16 |
| Smart App Control probed | done-verified | Registry value 0 (off); also the `security.smart_app_control` probe row |
| Repo layout, local `git init` (no remote; first commit `d0b3e7f` on 2026-11-04) | done-verified | branch `main`; `src/ tests/ catalogs/ tools/ docs/ third_party/` |
| One-command build/test | done-verified | `./build.ps1` (restore, build, banned-symbol coverage proof, analyzer-wiring proof, tests, smoke-gate self-test, smoke run) on the working tree 2026-10-17: 0 warnings, 562 tests, 41 capability rows, 13 probes, 0 failed; also green from a clean copy earlier the same stage |
| Pinned toolchain + .NET 8/9 ban | done-verified | `global.json`; `Directory.Build.props` target; test `Every_project_targets_net10_only` |
| Capability-probe prototype | done-verified | `jankscope probe` on the bench; one real defect found by running it (MDM false positive) and regression-tested. The review's weaknesses (#2, #3, #6–#8, #10, #12, #13, #17, #18) are fixed with tests and mutation checks; residuals are in `docs/LIMITATIONS.md` (L14–L18) |
| Synthetic harness skeleton (schedule generator) | done-unverified | Deterministic ground truth (own SplitMix64 generator, golden values pinned); no Direct3D renderer yet |
| Compatibility-fixture framework skeleton | done-unverified | Provenance type system + catalog validation; fixture-ingestion tool not started |
| Catalog framework (data-only, schema-validated) | done-verified | `CatalogTests` + `CatalogReviewTests`; hardened after review (#3 partly, #4, #5) |
| Static safety lints | done-verified | Rewritten after review #9: lexer-based `CSharpScanner` + `ProductLints` (native names incl. split literals, managed bans with a stale-proof allowlist, vendor words, exact `LibraryImport` set, System32-only search path, unsafe only in Platform, exact THIRD_PARTY rows, script/UTF-8/mojibake checks); 14 planted violations each turned the intended test red. Symbol-based `BannedApiAnalyzers` 5.6.0 adopted 2026-10-16 with the owner's yes (RS0030; `BannedSymbols.txt`; `tools/dev/verify-banned-symbols.ps1` proves every entry fires and runs in `build.ps1`); 15 planted violations, 9 lint mutations and 4 negative coverage runs were all caught (L17) |
| ADRs 0001–0007 | done-unverified | `docs/adr/`; ADR 0006 now Accepted (Vortice) |
| Competitive review, positioning, personas, success measures | done-unverified | `docs/BUSINESS.md` (web sources, not hands-on) |
| Risk register | done-verified | `docs/RISKS.md` |
| Cloud-test runbook | done-unverified | `docs/CLOUD_TESTING.md` skeleton; no IaC; terms unverified |
| Fact register | in progress | `docs/FACTS.md` |
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
| Overhead measurement v1 | done-verified | `tools/dev/Measure-Overhead.ps1` paired A-B-A-B benchmark; observer delta on median present error is 0.033 ms (< 0.200 ms budget); documented in `docs/benchmarks/overhead-v1.md` |

## Later stages
| Stage | State |
|---|---|
| 1b Interactive slice → **ready to begin (requires owner validation)** | not started |
| 2 Input and network | not started |
| 3 Expanded diagnosis | not started |
| 4 Controlled optimization → **stop for owner validation** | not started |
| 5 Hardening and release (+ go/no-go gate) | not started |
| P1 Cloud-VM compatibility campaign | not started (needs owner-created accounts) |

## Owner-only items outstanding (none blocks work before Stage 5)
See `OWNER_CHECKLIST.md`.
