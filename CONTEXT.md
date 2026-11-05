# CONTEXT: read this first

**Last updated: 2026-11-05.** This file exists so work survives chat compaction and new sessions. It is a short, current summary; `HANDOFF.md` is the long version and `docs/STATUS.md` is the evidence ledger.

**Update rule (for whoever is working):** change this file at the end of every task and before any risky or irreversible step. If it disagrees with reality, fix it. If something is unverified, say so. Keep it under 200 lines: move detail to `HANDOFF.md` / `docs/`.

## 1. What this is
**Jankscope**: a free, open-source (MIT), no-telemetry Windows tool that explains *why* a game stutters, a wireless mouse freezes or online play lags, using evidence-graded hypotheses. It changes a setting only when it can prove the benefit and undo it. It is not a "booster".
- Owner: Tejas (India). Reference PC: Windows 11 Home laptop, Intel + NVIDIA hybrid graphics, 6 SMT performance cores + 4 efficiency cores (10 cores / 16 threads), a kernel anti-cheat (Riot Vanguard) installed.
- Spec: `MASTER_PROMPT_v4.md` (hard constraints HC-1..HC-13 in section 5; stage plan in section 17).
- Repository: Published at `https://github.com/TejuBoricha/jankscope` (MIT). Branching strategy: **GitHub Flow** (`main` is protected with linear history and required CI checks; all work happens on `<type>/<slug>` branches merged via Squash/Rebase PRs — see `CONTRIBUTING.md`).

## 2. State right now
| Stage | State |
|---|---|
| 0 Bootstrap | **done-verified** (two independent reviews + a re-verification; `docs/reviews/stage-0*.md`) |
| 1a Headless core | **done-verified 2026-11-04**: all five work packages built and merged, independently reviewed and re-verified; exit tests, safety gates, incident reporting and overhead measurement are complete |
| 1b Interactive slice | **done-verified 2026-11-04**: hotkey marker, candidate anchors, incident recorder, capture session, two-layer redaction and export with preview, WPF app (`src/Jankscope.App`), the authors' own review (`docs/reviews/stage-1b.md`, all 6 findings fixed) and the independent review (`docs/reviews/stage-1b-independent.md`: 0 Critical, 0 High, 6 Medium, 11 Low; finding 2 fixed in #7, Medium 3 to 6 and Low 7 to 17 still open), live PresentMon + visible D3D11 harness E2E test, 10-run interleaved A/B overhead benchmark and app self-overhead check (`docs/benchmarks/overhead-v1.md`), and `docs/VALIDATION_1.md` complete. **Stopped at the Stage 1 owner validation gate** (`docs/VALIDATION_1.md`). |
| 2 Input and network | **in planning 2026-11-04**: ADR 0009 (`docs/adr/0009-stage-2-input-and-network.md`) and Stage 2 test plan (`docs/test-plans/stage-2.md`) written and reviewed; implementation starts after owner validation of Stage 1 |
| 3 to 5, P1 | not started (`MASTER_PROMPT_v4.md` section 17) |

Verified numbers (2026-11-04, full `build.ps1` locally and on GitHub Actions CI run `41867073761` on `main` `e20e8da`, exit 0; the CI totals were re-read from the job log on 2026-11-05): 0 warnings, **2,548 tests (2,547 passed, 1 skipped locally; 2,544 passed, 4 skipped on headless CI VM)** (805 Core, 1,099 Frames, 266 Harness, 344 Platform, 34 App), 0 failed; `jankscope probe` = **41 rows, 14 probes, 0 failed**; banned-symbol coverage **106 entries** proven; analyzer wiring proven for **7 product projects** (5 test projects exempt); **30 of 30** spec-listed deliverables present (`docs/` + `NOTICE` + root files). `jankscope-app.exe` self-overhead: `0.682%` CPU (`<= 1.0%`), `198.9 MB` RAM (`<= 250 MB`), `0` TCP/UDP endpoints.

## 3. Verify in two minutes
```
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
src\Jankscope.Cli\bin\Release\net10.0-windows\jankscope.exe probe
```
`build.ps1` runs: restore, build, banned-symbol coverage proof, analyzer-wiring proof, tests (one project at a time, 5-minute hang timeout), smoke-gate self-test, CLI smoke. About 2.5 minutes. dotnet is at `C:\Program Files\dotnet` (may not be on PATH; the script finds it). Use `-Clean` before trusting a mutation experiment (an incremental build can keep a mutated DLL).

## 4. Next actions, in order
1. Stage 1a and Stage 1b are complete (`docs/STATUS.md`, `docs/reviews/stage-1a-reverify.md`, `docs/reviews/stage-1b.md`).
2. **Before real-game numbers are read as evidence:** the independent review (`docs/reviews/stage-1b-independent.md`) asks for its Medium findings 3 (clock-uncertainty band is overconfident) and 4 (out-of-order rows are dropped silently and are absent from the export) to be fixed first, and calls 6 (a live capture of the harness is exported as `Measured`) the closest to High. Also open: Medium 5 (no HC-4 lint for managed key-reading code) and Low 7 to 17. This is code work and needs a Windows `build.ps1` run.
3. **Owner validation stop (`docs/VALIDATION_1.md`):** Have the owner run `jankscope-app.exe` against a real non-protected game (Aim Lab or PC Building Simulator) following `docs/VALIDATION_1.md`, then proceed to Stage 2 (Raw Input timing, display/GPU correlations, network/route probes).
4. Keep the remaining environment limitations explicit: true standard-user, VM, non-English, Windows 10, ARM64, elevated-token and real-game-session runs remain unexercised; see `docs/LIMITATIONS.md` and `docs/COMPATIBILITY.md`.

## 5. Standing rules from the owner
- Wants best-practice defaults chosen for them, **independent code review + testing at every stage** (gate: no open Critical/High), the best skill/agent/model per task, and **no waiting for a "go"**. Keep moving; save progress to files.
- **Branching & PR workflow (`CONTRIBUTING.md`):** Never push directly to `main`. Create a `<type>/<short-kebab-slug>` branch (`feat/*`, `fix/*`, `hotfix/*`, `docs/*`, `test/*`, `ci/*`, `chore/*`, `refactor/*`), run `.\build.ps1`, open a PR to `main`, wait for the GitHub Actions CI check to pass, and squash-merge.
- **Needs an explicit per-action yes** (ask once, concisely, keep working on other things): downloading any new third-party binary or package, creating accounts, spending money, publishing/posting/pushing, legal or tax matters, commits.
- Approved so far: .NET SDK 10.0.402 (winget), PresentMon 2.6.0 console exe (hash pinned), `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, `Vortice.DXGI` and `Vortice.Direct3D11` 3.8.3 with transitive `SharpGen.Runtime` / `.COM` 2.4.2-beta (all MIT, recorded in `docs/THIRD_PARTY.md`).
- Honesty: never fabricate a value; unknown is not off is not zero; label fixture/simulated data; say what was and was not tested. Never weaken a test to make it pass; prove safety tests can fail (mutation check).

## 6. How the code is guarded (do not fight these; read the failure)
- **Banned-API analyzer** (RS0030, an error): `BannedSymbols.txt` bans the whole `Process` type (one pragma exception for the name-only snapshot in `ServiceState.cs`, one for the runner's `PresentMonProcessLauncher.cs`), network types, registry writes, reflection and dynamic loading, COM interop, WMI methods, remote targets. A **method** entry needs the exact overload signature or it silently bans nothing; every entry needs a proof line in `tools/dev/analyzer-coverage/Coverage.cs.txt`. `DllImportAttribute` cannot be symbol-banned (the LibraryImport generator emits it).
- **Text lints** (`RepoLintTests`, `ProductLints`, `CSharpScanner`): native names, process members, vendor words (`catalogs/vendor_terms.json`), exact set of native imports (`ApprovedLibraryImports`, only `[LibraryImport("lit.dll", EntryPoint = "Lit")]`), System32-only DLL search, ASCII-only code, no `unsafe` outside `Jankscope.Platform`, one `Directory.Build.*` of each kind, product projects only under `src/` or `tools/`, packages versioned only in `Directory.Packages.props` with a matching `docs/THIRD_PARTY.md` row.
- **Optimize-mode gate** (HC-11): every input row must emit an exact value from `ManagedValues` or it blocks; `anticheat.state` is Unknown until a game-session detector exists. `mode.optimize` is therefore Recommend-only today (intended).
- **Build proofs**: `tools/dev/verify-banned-symbols.ps1`, `verify-analyzer-wiring.ps1`, `test-smoke-gate.ps1` are part of `build.ps1`.
- Exceptions need a written reason, an entry in `RepoLintTests` (`Allowed`, `BannedApiExceptions`, `ApprovedLibraryImports`) and independent review.

## 7. Pitfalls learned the hard way
- **Encoding:** Windows PowerShell 5.1 `Get-Content`/`Set-Content` corrupts UTF-8. The file-writing tools turn backslash-u escape sequences into literal characters: write `(char)0x...` or build the text from parts. A lint rejects mojibake and invalid UTF-8.
- **Shell quirks:** Git Bash heredocs can mangle backslashes and quotes (write a script file with the file tool and run it with Node); Node run from Git Bash cannot see bash's `/tmp`; the shell's working directory drifts after `cd` (always use absolute paths); `Jankscope.slnx` has CRLF line endings; `patch` fails on CRLF.
- **The file tools need a fresh Read** after a file changed on disk; "file changed" notices are usually your own earlier script.
- **Mutation experiments** (break a rule, see a test fail, restore) belong in a scratch copy of the repo, never in the live tree while another session reads it. Compile errors from "unreachable code" invalidate a mutation: use runtime-false conditions.
- **Timing tests** (frame pacing, sampler drift, watchdogs, ring stress) need the test projects to run one at a time; probes in `CapabilityMatrixBuilder` use `TaskCreationOptions.LongRunning` so a hung probe never starves the `.NET ThreadPool` on 2-vCPU CI runners.
- **WPF and the banned-API rule:** WPF generates a reflection helper (`GeneratedInternalTypeHelper.g.cs`, uses `Activator`) whenever XAML needs run-time wiring: an event handler inside a template, or a reference to a type of the same assembly. RS0030 rejects it, and no suppression route is allowed. So: no `Click=` inside a DataTemplate (attach one handler to the list in code) and create `TimelineView` in code, not in XAML.
- **Rendering check:** `JANKSCOPE_SNAPSHOT_DIR=<dir> dotnet test tests/Jankscope.App.Tests` writes PNG renders of every window and the timeline; look at them after any UI change.
- **Exports:** the guard checks decoded JSON strings, because a JSON-escaped path (`C:\\Users\\...`) is not the path as text. Known short user names (under 6 characters) match as whole words only (documented in `Redactor`).

## 8. Where things are
- `src/Jankscope.Core` (portable logic, catalogs loader, metrics, anti-cheat state machine; Stage 1b: `Hotkeys/`, `Reports/Redactor`, `ExportGuard`, `ExportPipeline`), `.Platform` (Windows probes, native imports, hardware/PDH; Stage 1b: `Hotkeys/` over `RegisterHotKey`), `.Frames` (frame samples, PresentMon parser/runner, ring buffer, stutter detector; Stage 1b: `Incidents/` clock map, anchors, recorder, bundle, timeline buckets, and `Capture/` session and program scanner), `.Cli` (`jankscope probe`, `incident`), `.App` (WPF: tray, hotkey window, timeline, investigator, export preview; `Services/AppHost` and `CaptureController` hold the logic and are tested without a screen).
- `tools/Jankscope.Harness` (D3D synthetic harness), `tools/Jankscope.FakePresentMon`, `tools/dev/*` (proof scripts and analyzer coverage), `tools/fetch-third-party.ps1`.
- `tests/*Tests` (Core, Platform, Frames, Harness, App), `tests/fixtures/presentmon` (one real capture + hand-written synthetic fixtures).
- `catalogs/*.json` (data-only knowledge: anti-cheat, protected titles, vendor terms, VM markers). `third_party/PresentMon` (pinned exe, git-ignored; lock file committed).
- Docs: `docs/STATUS.md` (ledger), `LIMITATIONS.md` (L1..L29), `FACTS.md` (claims and whether verified), `THIRD_PARTY.md`, `METRICS.md`, `CATALOGS.md`, `adr/0001..0007`, `reviews/`, `test-plans/stage-1a.md`, `handoff/agent-prompts/`.

## 9. Open items
- **Stage 1b defects resolved (2026-11-04):** All 6 findings from the authors' own Stage 1b review (`docs/reviews/stage-1b.md`, written in the same commit as its fixes, so not the independent gate) and all earlier open items (`Measure-Overhead.ps1` dynamic pass/fail + 10 interleaved runs, `IncidentReportBuilder` frame-time filtering + nullable metrics instead of fabricated zeros, stray `PresentMon-*.csv` cleanup, `jankscope-app.exe` self-overhead measurement) are fixed and verified. The separate independent review (`docs/reviews/stage-1b-independent.md`) still has open Medium findings 3 to 6 and Low findings 7 to 17.
- Owner-only (none blocks work before Stage 5): see `OWNER_CHECKLIST.md` (GitHub handle `jankscope`, trademark search for "Jankscope", Sponsors enrollment, optional Store registration, SignPath application, an optional standard Windows test account, beta testers, legal/accountant consults, publishing).
- Unverified facts: `docs/FACTS.md` (for example the .NET 10 end-of-support date, ETW session limit source, Smart App Control values 1/2, S-mode detection on a real S-mode device).
- Not yet exercised anywhere: elevated token, 32-bit process, VM, non-English Windows, Windows 10, ARM64, true standard-user account, real game session (`docs/LIMITATIONS.md` L3, L5, L19, L20).

## 10. Background and scratch
- **Current branch:** nothing in flight on `main` (`e20e8da`). The earlier `feat/bucket-docs-and-compliance-gates` was merged as #8 (30/30 spec deliverables, Criteria 19/21/22/23, Stage 2 ADR 0009 and test plan). A docs-only status correction from a cloud session sits on `ccr-8927703f-0vmag6`; that name does not match the CI branch-name gate, so rename it (for example `docs/status-corrections`) before opening a PR.
- Sessions started from the main one that are idle: `s1a-a-detector`, `s1a-b1-parser`, `s1a-b2-runner`, `s1a-c-platform`, `s1a-d-harness` and the Stage 0 re-review session. Their work is complete and should not be relaunched.
- Private copies of the packages: `C:\Users\tejas\Desktop\PC FPS booster - stage1a\` (safe to delete; they sit outside the repo on purpose because the lints scan everything under the repo root).
