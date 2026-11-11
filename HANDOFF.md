# HANDOFF — read this first in a new session

> **Start with `CONTEXT.md` (short, always current, survives chat compaction). This file is the detailed version; where the two disagree, `CONTEXT.md` and then `docs/STATUS.md` win.**

Updated 2026-11-05 (Stage 1b closed; at the Stage 1 owner validation stop), at a **safe point**: the repository builds with 0 warnings, CI on `main` (run 30, `228366d`) passes **2,660 tests (2,656 passed, 4 skipped)**, Stages 0, 1a, and 1b are complete and reviewed (`docs/reviews/stage-0*.md`, `docs/reviews/stage-1a*.md`, `docs/reviews/stage-1b.md` which is the authors' own review, and `docs/reviews/stage-1b-independent.md` which is independent: 0 Critical/High, all six Medium findings fixed, Low findings 7, 10 and 13 (and the visual part of 12) open), and nothing is half-edited. Everything a new session needs is in files; nothing depends on chat memory.

## 1. What this project is
**Jankscope** — a free, open-source (MIT), no-telemetry Windows tool that explains *why* a game stutters, a wireless mouse freezes, or online play lags, with evidence-graded hypotheses; it changes settings only when it can prove the benefit and undo the change. Owner: Tejas (India, Windows 11 Home laptop with Intel + NVIDIA hybrid graphics; kernel anti-cheat present). Funding: GitHub Sponsors + optional paid Store edition, **no ads**. Spec: `MASTER_PROMPT_v4.md`. Decisions are final in §2.2/§2.3/§4.1 of v4.

## 2. Verify the state in 2 minutes
```
cd "C:\Users\tejas\Desktop\PC FPS booster"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1     # restore, build (0 warnings), all tests, CLI smoke
src\Jankscope.Cli\bin\Release\net10.0-windows\jankscope.exe probe  # capability matrix of this PC
```
Expected: 845 Core + 347 Platform + 1,160 Frames + 267 Harness + 41 App = 2,660 tests (CI: 2,656 passed, 4 skipped; a local run on the reference PC has not been repeated since these changes); "banned-symbol coverage proven" (106 entries); "analyzer wiring proven for 7 product projects"; "smoke gate self-test passed"; "41 capability rows, 14 probes, 0 failed". (The ETW and Direct3D tests skip, with a visible reason, on an account or machine that cannot run them; here none of the ETW or D3D tests skip.)

**Git & Governance:** Published at `https://github.com/TejuBoricha/jankscope` under **GitHub Flow** (`main` protected with required CI status check, linear history, and Squash/Rebase merges; all changes land via `<type>/<short-kebab-slug>` topic branches and Pull Requests — see `CONTRIBUTING.md`).

## 3. Read order
1. This file → 2. `docs/STATUS.md` (ledger with evidence) → 3. `docs/reviews/stage-0.md` (open review findings) → 4. `docs/test-plans/stage-1a.md` → 5. `docs/FACTS.md` (which claims are verified) → 6. `docs/adr/*` → 7. `MASTER_PROMPT_v4.md` as needed (§5 hard constraints HC-1…HC-13 matter most).

## 4. Where we are
| Stage | State |
|---|---|
| 0 Bootstrap | **done-verified 2026-10-17** (owner's call). Two independent review passes and a re-verification: every finding fixed or recorded, no Critical/High open (`docs/reviews/stage-0*.md`). 562 tests; analyzer, lints, coverage proofs and smoke gate are part of `build.ps1`. |
| 1a Headless core | **done-verified 2026-11-04**. All five work packages (A detector, B1 parser, B2 runner, C hardware + PDH, D harness) were independently reviewed and re-verified. Exit tests, incident report, overhead v1, managed/game probes, anti-cheat gating, locale redaction and orphan-session cleanup are complete. |
| 1b Interactive slice | **done-verified 2026-11-04**. Global hotkeys, audible cue, capture clock map, candidate anchors, incident recorder, two-layer redaction (`Redactor` + `ExportGuard`), WPF app (`Jankscope.App`), the authors' review (`docs/reviews/stage-1b.md`) and an independent review (`docs/reviews/stage-1b-independent.md`, all six Medium findings fixed), live E2E test, 10-run interleaved A/B overhead benchmark, and `docs/VALIDATION_1.md` are complete. **Awaiting owner live-game validation (`docs/VALIDATION_1.md`).** |
| 2 … 5, P1 | not started |

Source map: `src/Jankscope.Core` (portable domain logic, redaction, catalogs) · `src/Jankscope.Platform` (Windows probes, P/Invoke, hotkeys, PDH, orphan cleaner) · `src/Jankscope.Frames` (ring buffer, stutter detector, PresentMon parser & supervised runner, clock map, candidate anchors, incident recorder/bundles) · `src/Jankscope.Cli` (`jankscope probe` / `incident` / `version`) · `src/Jankscope.App` (WPF desktop app, `TimelineView`, export preview, tray host) · `tools/Jankscope.Harness` (Direct3D 11 flip-model synthetic stutter harness) · `tools/Jankscope.FakePresentMon` (runner test double) · `catalogs/*.json` (data-only knowledge) · `third_party/PresentMon` (pinned binary, git-ignored; lock file committed) · `tools/fetch-third-party.ps1`.

## 5. Next actions, in order

### A. Stage 0 is closed: rules to keep (read before touching the lints, the analyzer or the gate)
Both review passes and the re-verification are done and the stage is `done-verified` (statuses and evidence: `docs/reviews/stage-0.md`, `stage-0-pass2.md`, `stage-0-pass2-reverify.md`).
1. **Working rules for the analyzer** (`Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, adopted with the owner's yes on 2026-10-16): ban a type where possible; a method entry needs the exact overload signature (otherwise it silently bans nothing, FACTS F29) plus a proof line in `tools/dev/analyzer-coverage/Coverage.cs.txt`; `build.ps1` runs `tools/dev/verify-banned-symbols.ps1` and fails if any entry is unproven. `DllImportAttribute` cannot be banned at symbol level (the LibraryImport generator emits it, FACTS F30); the source lints cover it. `build.ps1` also runs `tools/dev/verify-analyzer-wiring.ps1` (asks MSBuild whether every product project loads the analyzer; nested `Directory.Build.*` files and `ImportDirectory*` opt-outs are forbidden). New input rows for optimize mode must emit the exact values defined in `ManagedValues` or they block. The only RS0030 exception is the name-only process snapshot in `ServiceState.cs` (pinned by `RepoLintTests`); a runner that must launch PresentMon needs its own reviewed pragma, an entry in `BannedApiExceptions`, and an `Allowed` entry in `RepoLintTests`. New native imports must be `[LibraryImport("literal", EntryPoint = "literal")]` and appear in `ApprovedLibraryImports`.
2. Stage 1a completed the carried items: the orphan-ETW-session cleaner, `AntiCheatSafety.Decide` wiring, the game-session detector, real detection for `managed.applied_policies` and `managed.entra_join`, and the `os.locale` redaction flag (L16). Remaining limitations are recorded in `docs/LIMITATIONS.md`.

### B. Stage 1a: what is done and what is next
The five work packages (prompts in `docs/handoff/agent-prompts/`) were run as parallel sessions in private copies under `C:\Users\tejas\Desktop\PC FPS booster - stage1a\` (safe to delete) and **merged into the repo on 2026-10-17**; do not relaunch them. Their hand-over notes, review evidence and re-verification are summarised in `docs/STATUS.md`, `docs/reviews/stage-1a*.md` and `docs/LIMITATIONS.md`.

Next, in order:
1. Stages 1a and 1b are complete. Reviews and re-verifications are in `docs/reviews/stage-1a.md`, `docs/reviews/stage-1a-reverify.md`, and `docs/reviews/stage-1b.md`; the evidence ledger is `docs/STATUS.md`.
2. **Owner validation gate (`docs/VALIDATION_1.md`):** Wait for the owner's live validation on a real game (Aim Lab or PC Building Simulator), then begin Stage 2. The findings the independent review wanted fixed before those numbers count as evidence (3, 4 and 6) are fixed: `docs/reviews/stage-1b-independent.md`.
3. Preserve the documented unexercised environments and limitations in `docs/LIMITATIONS.md`.
4. Keep the build rules in section 5A. The timing-sensitive tests need the test projects to run one at a time (`build.ps1` does this with `-m:1`).

### C. Stage 1b and later
Follow `MASTER_PROMPT_v4.md` §17. **Stage 1b ends with a stop for the owner's validation** (`docs/VALIDATION_1.md`; default test game: Aim Lab, fallback PC Building Simulator, re-verified at that time — see §2.3).

## 6. Standing rules and the owner's directives
- Owner wants: **best-practice defaults chosen for them**, **independent code review + testing at every stage** (gate: no open Critical/High), the **best-suited skill/agent/model for each task**, and **no waiting for a "go"** — keep moving. Save progress to files.
- Autonomy does **not** override: downloading any new third-party binary, creating accounts, spending money, publishing/posting, pushing to a remote, or acting on legal/tax matters each need an explicit per-action yes. Approved so far: .NET SDK via winget (done); PresentMon 2.6.0 console exe (done, hash-verified). Nothing else.
- Hard constraints are enforced by tests: no hooks/injection/`OpenProcess`/driver loading, no network APIs, no vendor names outside `catalogs/`, .NET 10 only, every package recorded in `docs/THIRD_PARTY.md`. Never weaken a test to make it pass; prove safety tests can fail (mutation check).
- Honesty: never fabricate a value; unknown ≠ off ≠ zero; label fixture/simulated data; say what was and was not tested.
- **Encoding lesson:** Windows PowerShell 5.1 `Get-Content`/`Set-Content` corrupts UTF-8 (÷ × § became mojibake in one file this session). Use the Read/Edit/Write tools, Node or Bash for text edits. The shell's working directory drifts after `Set-Location` — always set it explicitly to the repo. A second trap, caught by the new `Text_files_are_valid_utf8_with_no_mojibake` lint: the Write/Edit tools turn `\uXXXX` escape sequences in the text you give them into literal characters — never write `\u` escapes into source; use `(char)0x…` or check with `od -c`. Node run from Git Bash cannot see bash's `/tmp`; use a scratchpad path converted with `cygpath -m`.
- Run `RepoLintTests` locally before finishing any change; run `build.ps1 -Clean` from a clean copy before closing a stage.

## 7. Owner-only items (none blocks work before Stage 5)
See `OWNER_CHECKLIST.md`: GitHub account/handle `jankscope`, trademark search for "Jankscope", Sponsors enrollment, optional Store registration, SignPath application, standard Windows test account (optional), beta testers, legal/accountant consults, publishing. The agent prepares drafts; the owner posts/publishes.

## 8. Open facts (see `docs/FACTS.md`)
Unverified: .NET 10 exact end-of-support date; ETW 64-session limit source; Smart App Control values 1/2; WinUI 3 gaps; Store revenue share and packaged-app elevation; GitHub Sponsors payout fees; Azure signing eligibility (re-check at Stage 5); `runas /trustlevel` value; S-mode detection on Windows 11 (use `WindowsIntegrityPolicy` or verify on a real S-mode device).

## 9. Memory
Persistent notes live in `C:\Users\tejas\.claude\projects\C--Users-tejas-Desktop-PC-FPS-booster\memory\` (user preferences and project decisions); they point back to this file.
