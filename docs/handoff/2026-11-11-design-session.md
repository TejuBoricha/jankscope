# Handoff: design session, 2026-11-11

Read this first in a new session. It says where the work stands, what is merged, what is open, and what to do next.
`CONTEXT.md` and `HANDOFF.md` describe the stage plan from before this session. This file is the current state of the
redesign. Where they disagree on the design, this file wins.

Written at a safe point: nothing is half-edited, every branch that matters is pushed, and the one open PR is described below.

---

## 1. The project in one paragraph

Jankscope is a free, open-source (MIT), no-telemetry Windows tool (WPF, .NET 10). It explains why a game stutters or input
stalls, using documented user-mode interfaces only (PresentMon ETW, PDH, RegisterHotKey). The hard constraints HC-1 to
HC-13 are binding: no hooks, no injection, no overlays, no drivers, no network, no telemetry. Output is evidence-graded
(facts, inferences, hypotheses). The owner's ambition is a broader PC hardware platform (see section 6).

## 2. Verify the state in two minutes (Windows)

```
winget install --id Microsoft.DotNet.SDK.10 --source winget --accept-package-agreements --accept-source-agreements
cd jankscope
./build.ps1
dotnet run --project src/Jankscope.App -c Release
```

Full steps, including the optional ETW group, are in `docs/INSTALL.md`.

## 3. What is merged on `main` this session

| PR | Commit on main | What it is |
|---|---|---|
| #21 | `962669369` | Design audit and direction (phase 0): `docs/design/AUDIT.md`, `docs/design/DIRECTION.md` |
| #22 | `15bac9d` | Phase 1: names, sidebar labels, HC codes out of the UI, PRO badge removed |
| #23 | `4415f99` | Phase 2: shorter KPI labels and card headers (fixes clipping and collisions on Watch) |
| #24 | `1f49c00` | `docs/design/PLATFORM.md`: platform foundation for the wider product |
| #25 | `246ea41` | Phase 2: Replay candidate column width, causes title, rebalanced columns |
| #26 | `48cefb4` | Phase 2: Watch mark buttons fit the toolbar (narrower target box, tighter margins) |
| #27 | `d82084a` | Phase 2: compact pacing tier labels on Replay ("SMOOTH", "TIERS") |
| #28 | `80fd2ae` | `docs/design/SYSTEM.md`: the design system specification |

`main` CI: the runs for #21 to #26 passed. The run for #27 (`d82084a`) was still `in_progress` when this file was
written. **Check it first** (section 5).

## 4. Open work

### 4a. PR #29: signal bench visual theme (`feat/signal-theme`, head `5ba3148`)

- Dark and light themes rewritten with the same token keys. Warm graphite (dark) and warm paper (light) surfaces,
  one phosphor-lime colour for live data (olive in light), amber for marks, indigo for simulated data.
- Bahnschrift for headings and KPI numbers, falling back to Segoe UI. Heading 20 to 24, KPI value 21 to 26.
- The `PurpleAccent` keys now hold the amber mark colour. The name is wrong; renaming is a later cleanup.
- First CI run failed on one test, `TimelineViewTests.A_one_frame_spike_is_drawn_as_a_tall_line_not_a_dot`. The test
  counted a drawn line by its blue channel, which only the old cyan line has. Commit `5ba3148` changes the test to any
  bright channel. The background stays dark in both themes, so it still does not count. **That re-run was in progress
  when this file was written.**
- Status of the review: not yet reviewed on screen. Next step: download `app-window-renders` from the run for `5ba3148`,
  check every screen myself (Watch, Replay, Capability check, Input lab, Settings, Trust, export review, Welcome), and
  compare with `main`'s renders. Merge only when the screens read well and CI is green.

### 4b. Known issues, not yet fixed

1. **Replay root-cause title is cut off** ("Frame pacing hitc…"). The text comes from code (the rules engine's finding
   title), so the fix is a layout change or a shorter finding title in the code. Not started.
2. **`D3dHarnessTests` are timing-flaky on CI.** They run a WARP software rasterizer and check frame-timing tolerances
   (for example "p99 Present() error 17.463 ms > 16 ms"). A run on PR #27 failed on them with the same code that passed
   on re-run. Project rules allow one re-run; do not skip or quarantine the tests. Next step: make the tolerances
   robust (measure over a longer window, or assert medians), in a separate PR.
3. **Design-system implementation** (phase 2 proper, per `docs/design/SYSTEM.md`) is not started beyond the
   merged label and layout fixes:
   - Type scale: 14 font sizes are still used in the views. Map them to the five-step scale.
   - Inline `FontFamily="Consolas"` (42 uses) should become a token.
   - Lint rules (no inline colour or size literals in views; keys match across themes; spacing values) are not in CI.
   - Components in `SYSTEM.md` section 6.2 (Reading, Finding card, and so on) are not built.
4. **Light theme** has only just been rewritten, in PR #29. It has not been reviewed on screen.
5. **Watch "CH" and "//" codes** are mostly gone from KPI labels, but some card captions still use the "//" style. Check
   them in the next review.

### 4c. Owner decisions still open (do not decide these for the owner)

1. **Constraint question (`docs/design/PLATFORM.md` section 1).** Automatic tuning, fan control, voltage control and
   power-limit changes need writes to hardware, which conflict with HC-1 (documented user-mode interfaces only) and HC-2
   (no third-party kernel drivers). Option A: Jankscope stays an observer and advisor. Option B: a narrow, signed write
   path, which needs an ADR and a change to `docs/RISKS.md` first. Nothing in the platform document depends on Option B.
2. **Sensor feasibility spike.** Approve a short spike on reference machines to see which temperature, voltage, fan and
   power readings are reachable through documented user-mode interfaces. Many are not, as far as we know. Unverified.
3. **Competitor review.** `docs/design/PLATFORM.md` section 11 is from general knowledge, not a current review.
4. **skill-issue.in MCP.** The owner provided `https://skill-issue.in/api/mcp`. It is **not connected** in this session,
   and no tool from it is available. The owner must add it as a connector before it can be used. Sending repository
   material (design docs, screenshots, source) to any external service needs the owner's explicit approval first.
5. **Theme naming.** The owner asked for a bolder identity. PR #29 is my choice. If the owner wants another direction,
   it's a token-value change only (keys are stable).

## 5. First actions for the next session

1. Check CI on `main` for `d82084a` (PR #27). Expected: passes. If it fails, root-cause it before anything else.
2. Check CI on PR #29 (`5ba3148`). If it is green, download `app-window-renders` and review every screen against `main`.
   Merge if the screens are good. If it is red, find out whether the failure is in the signal-theme change; if it is in
   the flaky harness tests, re-run once, then fix the tolerances (section 4b.2).
3. Start the design-system implementation in the order in `docs/design/SYSTEM.md` section 8, one PR per step.

## 6. Working rules (the owner's instructions, keep them)

- **Merge green PRs automatically.** Do not wait for approval on minor decisions. Stop only for the decisions in 4c.
- **One PR per phase or fix**, with the render review done by me, on screen, before merging.
- **After each merge, check `main`'s own CI** and report if it fails.
- **Red CI on a PR I opened is my work.** Find the cause. Re-run a job at most once to test for a flake. Never skip,
  disable or quarantine a test to get green, and never push an empty commit to kick CI.
- **Branch names** must match `^((feat|fix|hotfix|docs|test|ci|chore|refactor)/[a-z0-9][a-z0-9._-]*|dependabot/...)$`.
  `design/` is not allowed (PR #20 was closed for that).
- **Squash-merge** into `main` with `expectedHeadSha` set to the reviewed head.
- **Commit trailers:** `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>` and the `Claude-Session` line. PR bodies
  end with the "Generated with Claude Code" line. No model identifiers in code, comments, commit messages or PR text
  beyond those trailers.
- **Do not create a PR** for work the owner has not asked for. Phase work and fixes the owner has asked for are
  authorised.
- **Private data:** the owner's email is private. Never put it in a request to an external service.

## 7. Environment and tooling notes (Linux sandbox)

- The app cannot run here (WPF is Windows-only). Compile checks use the pinned SDK with
  `-p:EnableWindowsTargeting=true`:
  - `dotnet build src/Jankscope.App -p:EnableWindowsTargeting=true`
  - `dotnet test tests/Jankscope.Core.Tests -p:EnableWindowsTargeting=true` (849 tests; includes the repository lint
    tests, which also check that `.cs` files are ASCII)
  - Platform and Frames suites: same failures as `main` on Linux (Windows paths and processes). Compare against `main`,
    not against zero.
- The SDK used in the session was extracted to the scratchpad `dotnet/` folder. It may not exist in a new session.
  Install SDK 10.0.402 from `global.json` (winget on Windows, `dotnet-install` on Linux).
- **Render artifacts:** the CI job `Render the app's windows to PNG` uploads `app-window-renders`. The artifact is
  downloadable without authentication in this environment:
  `https://api.github.com/repos/TejuBoricha/jankscope/actions/artifacts/<id>/zip`. Find the id with the GitHub MCP tool
  `actions_list` (`list_workflow_run_artifacts`), or from the check-run `details_url`.
- **CI status by commit:** `https://api.github.com/repos/TejuBoricha/jankscope/commits/<sha>/check-runs`.
- **Job logs:** GitHub MCP `get_job_logs` with `return_content: true`. Use the job id from the check-run URL.
- Screens reviewed in this session were read from the render PNGs with the image viewer. A render is a snapshot of
  the window at a fixed size (1044 x 788); it is not a live run.
- The CI windows use test fixture data (for example "Title of anticheat.state" on Capability check). Judge layout
  on those, and judge real data separately.

## 8. Where the design decisions live

- `docs/design/AUDIT.md`: what exists today, 25 findings, the behaviour that must be kept.
- `docs/design/DIRECTION.md`: the look, the navigation (Watch, Replay, Share, Tools, Settings, Trust), the phases.
- `docs/design/PLATFORM.md`: the platform foundation, module contract, experience patterns, and the constraint question.
- `docs/design/SYSTEM.md`: tokens, type scale, spacing, motion, components, enforcement, phase 2 order.
- `docs/RULES.md`, `docs/OPTIMIZATIONS.md`, `docs/RISKS.md`, `docs/THIRD_PARTY.md`: the diagnostic rules and the safety
  rules that bind all of the above.
