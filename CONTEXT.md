# CONTEXT: read this first

**Last updated: 2026-11-04.** This file exists so work survives chat compaction and new sessions. It is a short, current summary; `HANDOFF.md` is the long version and `docs/STATUS.md` is the evidence ledger.

**Update rule (for whoever is working):** change this file at the end of every task and before any risky or irreversible step. If it disagrees with reality, fix it. If something is unverified, say so. Keep it under 200 lines: move detail to `HANDOFF.md` / `docs/`.

## 1. What this is
**Jankscope**: a free, open-source (MIT), no-telemetry Windows tool that explains *why* a game stutters, a wireless mouse freezes or online play lags, using evidence-graded hypotheses. It changes a setting only when it can prove the benefit and undo it. It is not a "booster".
- Owner: Tejas (India). Reference PC: Windows 11 Home laptop, Intel + NVIDIA hybrid graphics, 6 SMT performance cores + 4 efficiency cores (10 cores / 16 threads), a kernel anti-cheat (Riot Vanguard) installed.
- Spec: `MASTER_PROMPT_v4.md` (hard constraints HC-1..HC-13 in section 5; stage plan in section 17). v3 and `PROMPT_REVIEW.md` are history.
- Funding: GitHub Sponsors + optional paid Store edition; **no ads**. Repo is local only, **no remote**. First commit `d0b3e7f` (2026-11-04, at the owner's request); commit again only when the owner asks, and never push without a per-action yes. Three stray `PresentMon-*.csv` captures in the repo root are deliberately left uncommitted (raw captures from this PC, contents not inspected).

## 2. State right now
| Stage | State |
|---|---|
| 0 Bootstrap | **done-verified** (two independent reviews + a re-verification; `docs/reviews/stage-0*.md`) |
| 1a Headless core | **done-verified 2026-11-04**: all five work packages built and merged, independently reviewed and re-verified; exit tests, safety gates, incident reporting and overhead measurement are complete |
| 1b Interactive slice | **built and tested, not yet reviewed or run live** (ADR 0008, `docs/test-plans/stage-1b.md`): hotkey marker, candidate anchors, incident recorder, capture session, redaction and export with preview, WPF app (`src/Jankscope.App`). Still to do: live end-to-end run against the harness, independent review (`docs/reviews/stage-1b.md`), `docs/VALIDATION_1.md`, then **the owner's validation stop** (default test game Aim Lab, fallback PC Building Simulator, re-verify at that time) |
| 2 to 5, P1 | not started (`MASTER_PROMPT_v4.md` section 17) |

Verified numbers (2026-11-04, full `build.ps1`, exit 0): 0 warnings, **2,498 tests** (764 Core, 1,094 Frames with 1 skipped symlink test, 266 Harness, 344 Platform, 30 App), 0 failed; `jankscope probe` = **41 rows, 14 probes, 0 failed**; banned-symbol coverage **106 entries** proven; analyzer wiring proven for **7 product projects** (5 test projects exempt). The earlier figures in this file (2,122 tests, 13 probes, 6 projects) were stale; the baseline at the start of the Stage 1b session was also red (a non-ASCII byte in `tools/dev/Measure-Overhead.ps1`, fixed).
Progress report (private artifact, graded against the spec's 35 acceptance criteria): about 25% weighted, 11% fully verified; see `https://claude.ai/artifact/TiwMCmccHbV5f8f1rm396M`. Its grades are judgements with evidence attached.

## 3. Verify in two minutes
```
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
src\Jankscope.Cli\bin\Release\net10.0-windows\jankscope.exe probe
```
`build.ps1` runs: restore, build, banned-symbol coverage proof, analyzer-wiring proof, tests (one project at a time, 5-minute hang timeout), smoke-gate self-test, CLI smoke. About 4 minutes. dotnet is at `C:\Program Files\dotnet` (may not be on PATH; the script finds it). Use `-Clean` before trusting a mutation experiment (an incremental build can keep a mutated DLL).

## 4. Next actions, in order
1. Stage 1a is complete. Its independent review, security fixes, re-verification, exit-test variants, anti-cheat gate, orphan-session cleaner, incident report, and overhead benchmark are recorded in `docs/STATUS.md` and `docs/reviews/stage-1a-reverify.md`.
2. Close Stage 1b: (a) run the real app against the harness with live PresentMon and press the chord after an injected hitch (nobody has run the app live yet; the audible tone has never been heard); (b) independent review, fix every finding; (c) fix the known open items below; (d) measure the app's own CPU and memory while capturing (HC-6); (e) write `docs/VALIDATION_1.md` and bring STATUS, LIMITATIONS and HANDOFF up to date (they still describe Stage 1a); (f) ask the owner for the validation on a real non-protected game (the work is committed locally as `d0b3e7f`; commit the fixes from (b) and (c) when asked).
3. Keep the remaining environment limitations explicit: true standard-user, VM, non-English, Windows 10, ARM64, elevated-token and real-game-session runs remain unexercised; see `docs/LIMITATIONS.md` and `docs/COMPATIBILITY.md`.

## 5. Standing rules from the owner
- Wants best-practice defaults chosen for them, **independent code review + testing at every stage** (gate: no open Critical/High), the best skill/agent/model per task, and **no waiting for a "go"**. Keep moving; save progress to files.
- **Needs an explicit per-action yes** (ask once, concisely, keep working on other things): downloading any new third-party binary or package, creating accounts, spending money, publishing/posting/pushing, legal or tax matters, commits.
- Approved so far: .NET SDK 10.0.402 (winget), PresentMon 2.6.0 console exe (hash pinned), `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0. `Vortice.DXGI` and `Vortice.Direct3D11` 3.8.3 with their transitive packages (`SharpGen.Runtime` and `.COM` are pre-release 2.4.2-beta) were restored by the Stage 1a sessions as ADR 0006 planned; on 2026-10-17 the owner delegated the decision ("it is up to you") and they are **accepted** (all MIT per their nuspecs, recorded in `docs/THIRD_PARTY.md`). Revisit when a stable SharpGen release appears or at any package update.
- Honesty: never fabricate a value; unknown is not off is not zero; label fixture/simulated data; say what was and was not tested. Never weaken a test to make it pass; prove safety tests can fail (mutation check).
- Connected MCP connectors: none needed through Stage 1a. Telemetry-type ones (Datadog, Amplitude, Pendo, PagerDuty) conflict with the no-telemetry rule. Figma may help the Stage 1b UI design; GitHub only for the owner-approved publishing step. Pricing of connectors was not checked.

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
- **Timing tests** (frame pacing, sampler drift, watchdogs, ring stress) need the test projects to run one at a time; a one-off Frames hang under parallel load was never traced to a test.
- A new test that asserts the old wording of a message will fail after a wording fix: update the test, not the rule.
- **WPF and the banned-API rule:** WPF generates a reflection helper (`GeneratedInternalTypeHelper.g.cs`, uses `Activator`) whenever XAML needs run-time wiring: an event handler inside a template, or a reference to a type of the same assembly. RS0030 rejects it, and no suppression route is allowed. So: no `Click=` inside a DataTemplate (attach one handler to the list in code) and create `TimelineView` in code, not in XAML. `UseWPF` also removes the `System.IO` implicit using (re-added in the csproj) and `UseWindowsForms` needs `NoWarn WFO0003` for the manifest DPI setting.
- **Shell working directory:** a shell whose cwd drifts into a folder (it did into `src/Jankscope.App/obj/...`) locks it, so `Remove-Item` fails silently and a stale generated file survives a "clean". Use absolute paths and `cd` to the repo root before deleting. A scratchpad glob delete is blocked by the sandbox.
- **Rendering check:** `JANKSCOPE_SNAPSHOT_DIR=<dir> dotnet test tests/Jankscope.App.Tests` writes PNG renders of every window and the timeline; look at them after any UI change. A test that cannot fail proves nothing: the timeline spike test first passed with the drawing removed (mutation check) and had to be rewritten.
- **Exports:** the guard checks decoded JSON strings, because a JSON-escaped path (`C:\\Users\\...`) is not the path as text. Known short user names (under 6 characters) match as whole words only (documented in `Redactor`).

## 8. Where things are
- `src/Jankscope.Core` (portable logic, catalogs loader, metrics, anti-cheat state machine; Stage 1b: `Hotkeys/`, `Reports/Redactor`, `ExportGuard`, `ExportPipeline`), `.Platform` (Windows probes, native imports, hardware/PDH; Stage 1b: `Hotkeys/` over `RegisterHotKey`), `.Frames` (frame samples, PresentMon parser/runner, ring buffer, stutter detector; Stage 1b: `Incidents/` clock map, anchors, recorder, bundle, timeline buckets, and `Capture/` session and program scanner), `.Cli` (`jankscope probe`, `incident`), `.App` (WPF: tray, hotkey window, timeline, investigator, export preview; `Services/AppHost` and `CaptureController` hold the logic and are tested without a screen).
- `tools/Jankscope.Harness` (D3D synthetic harness), `tools/Jankscope.FakePresentMon`, `tools/dev/*` (proof scripts, `apply-edits.js`, analyzer coverage), `tools/fetch-third-party.ps1`.
- `tests/*Tests` (Core, Platform, Frames, Harness), `tests/fixtures/presentmon` (one real capture + hand-written synthetic fixtures).
- `catalogs/*.json` (data-only knowledge: anti-cheat, protected titles, vendor terms, VM markers). `third_party/PresentMon` (pinned exe, git-ignored; lock file committed).
- Docs: `docs/STATUS.md` (ledger), `LIMITATIONS.md` (L1..L29), `FACTS.md` (claims and whether verified), `THIRD_PARTY.md`, `METRICS.md`, `CATALOGS.md`, `adr/0001..0007`, `reviews/`, `test-plans/stage-1a.md`, `handoff/agent-prompts/` (how the 1a packages were specified; reuse the style for later parallel work).
- Persistent notes outside the repo: `C:\Users\tejas\.claude\projects\C--Users-tejas-Desktop-PC-FPS-booster\memory\` (they point back to this file).

## 9. Open items
- **Known defects found by this session's progress review (not yet fixed):** `tools/dev/Measure-Overhead.ps1` prints `**PASS**` as fixed text and used 2+2 runs against a 10+ run protocol (so Stage 1a "overhead v1" is re-graded partial); `IncidentReportBuilder` turns an uncomputable metric into 0 (the `jankscope incident` path is exposed; the new export path filters bad frames first); three stray `PresentMon-*.csv` captures sit untracked in the repo root (contents not inspected); the app's own overhead is unmeasured. Frames with present mode "Other" are ignored by the capture session and counted (same rule as the CLI): confirm that is right for real games.
- Owner-only (none blocks work before Stage 5): see `OWNER_CHECKLIST.md` (GitHub handle `jankscope`, trademark search for "Jankscope", Sponsors enrollment, optional Store registration, SignPath application, an optional standard Windows test account, beta testers, legal/accountant consults, publishing).
- Domain (2026-10-18): jankscope.tech was available at get.tech; the owner's Hacktoberfest code made year 1 free (renews at INR 2,847.82/year). The owner is completing the order and any account, details and payment steps themselves (the agent stopped at the cart, privacy toggle off, and does not handle accounts or payment). The agent has not confirmed the order went through. The name is still not trademark-cleared.
- Unverified facts: `docs/FACTS.md` (for example the .NET 10 end-of-support date, ETW session limit source, Smart App Control values 1/2, S-mode detection on a real S-mode device).
- Not yet exercised anywhere: elevated token, 32-bit process, VM, non-English Windows, Windows 10, ARM64, true standard-user account, real game session (`docs/LIMITATIONS.md` L3, L5, L19, L20).

## 10. Background and scratch
- **Running now:** none. Stage 1a review and re-verification are complete.
- Sessions started from the main one that are idle: `s1a-a-detector`, `s1a-b1-parser`, `s1a-b2-runner`, `s1a-c-platform`, `s1a-d-harness` and the Stage 0 re-review session. Their work is complete and should not be relaunched.
- Private copies of the packages: `C:\Users\tejas\Desktop\PC FPS booster - stage1a\` (safe to delete; they sit outside the repo on purpose because the lints scan everything under the repo root).
