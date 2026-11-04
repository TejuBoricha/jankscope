# Contributing to Jankscope

Thank you for contributing to **Jankscope** — a free, open-source (MIT), zero-telemetry diagnostic oscilloscope for Windows gaming PCs.

Because Jankscope runs alongside games protected by kernel-level anti-cheat systems and handles local diagnostic data, **every contribution — whether from the project owner, a team member, an external developer, or an AI coding agent — follows the same branching, verification, and safety governance.**

---

## 1. Git Branching Strategy (GitHub Flow)

Jankscope uses **GitHub Flow** with short-lived, typed topic branches and tag-driven releases. We deliberately avoid long-lived `develop` or `release/*` branches to keep integration fast, linear, and conflict-free.

```
main  ●───────────●───────────────────●───────────●  (tag: v0.2.0)
       \         / \                 /
        ●───●───●   ●───────●───────●
        feat/raw-input      fix/clock-map-anchor
```

### Branch Classification

| Branch Pattern | Lifespan | Originates From | Merges Into | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **`main`** | **Permanent** | Initial commit | — | Protected, production-ready trunk. Always compiles with `0` warnings and passes all proofs, tests, and smoke gates. |
| **`feat/<slug>`** | Temporary | `main` | `main` | New diagnostic collectors, probes, UI views, or stage work packages (e.g., `feat/stage2-raw-input-probe`). |
| **`fix/<slug>`** | Temporary | `main` | `main` | Bug fixes or code-review remediations (e.g., `fix/clock-map-restart-anchor`). |
| **`hotfix/<slug>`** | Temporary | `main` (or `vX.Y.Z` tag) | `main` | Urgent fixes for defects discovered in a published release. |
| **`docs/<slug>`** | Temporary | `main` | `main` | Documentation, ADRs, benchmark reports, or review ledgers (e.g., `docs/overhead-benchmark-v1`). |
| **`test/<slug>`** | Temporary | `main` | `main` | Unit, integration, fuzzing, or mutation-proof tests (e.g., `test/redactor-fuzz-cases`). |
| **`ci/<slug>`** / **`chore/<slug>`** | Temporary | `main` | `main` | Build scripts, GitHub Actions, catalog updates, or pinned dependency updates (e.g., `ci/branch-governance`). |
| **`refactor/<slug>`** | Temporary | `main` | `main` | Internal code restructuring that preserves behavior and all hard constraints. |

### Branch Naming Rules (Enforced by CI)
- Every Pull Request branch **must** match:
  ```text
  ^(feat|fix|hotfix|docs|test|ci|chore|refactor)/[a-z0-9][a-z0-9._-]*$
  ```
- Use lowercase kebab-case slugs (2–5 words).
- **Good:** `feat/raw-input-latency`, `fix/presentmon-restart-preroll`, `docs/stage-1b-review`
- **Rejected by CI:** `my-branch`, `Tejas/patch`, `feature/NewThing`, `patch-1`

---

## 2. Universal 10-Step Contribution Lifecycle

Every contributor (human developer or AI coding agent) must follow this exact lifecycle:

1. **Sync with `main`:**
   ```powershell
   git checkout main
   git pull --ff-only origin main
   ```
2. **Create a properly named working branch:**
   ```powershell
   git checkout -b feat/short-descriptive-slug
   ```
3. **Make focused, logical commits** using **Conventional Commits** format:
   - `<type>(<optional-scope>): <imperative summary>`
   - Examples:
     - `feat(platform): add Raw Input device registration probe`
     - `fix(frames): clear candidate history across capture session restarts`
     - `test(core): add decoded-JSON unicode escape redaction guard tests`
4. **Run the full local verification suite (`build.ps1`):**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   ```
   Do **not** push if `build.ps1` reports any warning, banned-symbol proof failure, analyzer wiring failure, test failure, or smoke-gate error.
5. **Push the working branch to `origin`:**
   ```powershell
   git push -u origin feat/short-descriptive-slug
   ```
6. **Open a Pull Request targeting `main`:**
   - Fill out `.github/PULL_REQUEST_TEMPLATE.md` completely (Summary, Related Issue, Change Type, and Hard-Constraint Checklist).
   - Via CLI: `gh pr create --base main --fill`
7. **Wait for automated GitHub Actions CI checks:**
   - The required status check **`Windows x64 (.NET 10 SDK | Safety Proofs | 2,505 Tests | Smoke Gate)`** validates branch naming, compiles in Release mode with `TreatWarningsAsErrors=true`, runs both static analyzer proofs, executes all 2,505+ tests, and runs the live `jankscope probe --json` smoke gate.
8. **Resolve review feedback and keep the branch up to date:**
   - All review conversations must be resolved before merging.
   - If `main` has advanced, update your branch via rebase (`git fetch origin && git rebase origin/main`) rather than merging `main` into your branch.
9. **Merge using Squash Merge (Default) or Rebase Merge:**
   - **Squash and merge** is the repository default: each PR becomes one atomic, verifiable commit on `main`.
   - **Rebase and merge** may be used when a multi-package PR intentionally preserves distinct, self-contained commits that each pass `build.ps1`.
   - **Merge commits (`--no-ff`) are disabled** on the repository to enforce a strictly linear history on `main`.
10. **Clean up the merged branch:**
    - Remote branches are deleted automatically by GitHub upon PR merge (`delete_branch_on_merge = true`).
    - Prune your local tracking branch:
      ```powershell
      git checkout main
      git pull --ff-only origin main
      git branch -d feat/short-descriptive-slug
      git fetch --prune
      ```

---

## 3. Releases and Emergency Hotfixes

### Cutting a Release
- Releases are cut directly from a verified commit on `main` using annotated semantic version tags (`vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-preview.N`):
  ```powershell
  git checkout main
  git pull --ff-only origin main
  git tag -a v0.2.0 -m "Jankscope v0.2.0"
  git push origin v0.2.0
  ```
- Long-lived `release/*` branches are not used unless a major stage requires a multi-day stabilization freeze while `main` moves ahead.

### Emergency Hotfixes
1. Branch `hotfix/<slug>` from `main` (or from the affected `vX.Y.Z` tag if `main` already contains unreleased breaking changes).
2. Add a regression test that reproduces the defect, apply the fix, and verify with `.\build.ps1`.
3. Open a Pull Request to `main`, wait for CI to pass, squash-merge, and tag the new patch version (`vX.Y.(Z+1)`).

---

## 4. Non-Negotiable Hard Constraints (`HC-1` .. `HC-13`)

Every line of product code (`src/` and `tools/`, excluding `tests/`) must obey the hard constraints in [`MASTER_PROMPT_v4.md`](MASTER_PROMPT_v4.md) section 5:

| Constraint | Rule | How It Is Enforced |
| :--- | :--- | :--- |
| **`HC-1` Anti-Cheat Safety** | Never inject DLLs, install hooks (`SetWindowsHookEx`), draw in-game overlays, attach debuggers, or open foreign process handles (`OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`). | `BannedSymbols.txt` (`RS0030`) + `RepoLintTests` (`CSharpScanner` lexer scan) |
| **`HC-2` Kernel & Driver Safety** | Never ship, load, or extract a kernel driver (`CreateService`, `NtLoadDriver`), and never bundle vulnerable third-party hardware drivers. | `BannedSymbols.txt` + `ApprovedLibraryImports` exact-set gate |
| **`HC-4` Zero Telemetry & Zero Unapproved Network** | Zero telemetry, analytics, crash uploaders, or auto-updaters. All `System.Net.*` socket/HTTP APIs are banned at compile time in Stage 1. | `BannedSymbols.txt` + runtime `0 TCP / 0 UDP` verification |
| **`HC-6` Self-Overhead Budget** | Desktop app CPU `<= 1.0%`, working set `<= 250 MB`, observer-effect `Present()` median disturbance `< 0.200 ms`, `p99 < 0.500 ms`, frame-time median `< 0.100 ms`. | `tools/dev/Measure-Overhead.ps1` (10-run interleaved A-B-A-B benchmark) |
| **`HC-7` Child Process Pinning** | Only the pinned PresentMon console executable may be launched, verified by SHA-256 on an open `FileStream` handle before start, with an argv allowlist that refuses `--restart_as_admin`. | `FileHashVerifier`, `PresentMonArguments`, `LauncherLintTests` |
| **`HC-8` No Dynamic Code Loading** | No `Assembly.Load*`, `Activator.CreateInstance`, `Type.GetType`, dynamic P/Invoke (`LoadLibrary`, `GetProcAddress`), or MSBuild `<Exec>` tasks. | `BannedSymbols.txt` + `RepoLintTests` |
| **`HC-9` Provenance Honesty** | Every data point carries `DataProvenance` (`Measured`, `Inferred`, `Simulated`, `Fixture`). Simulated or fixture data can never be exported as a real measurement. | `ProvenanceRules` + `IncidentBundleBuilder` |
| **`HC-10` Dependency Pinning** | Central package management (`Directory.Packages.props`) must match [`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md) exactly. | `RepoLintTests.Package_references_are_centrally_versioned...` |
| **`HC-12` Catalog-Driven Knowledge** | Vendor strings, anti-cheat service names, and protected game titles belong in `catalogs/*.json`, never hardcoded in C# product code. | `RepoLintTests.Product_code_names_no_vendor_outside_catalogs...` |

---

## 5. Technical & AI-Agent Contribution Rules

Whether you are a human engineer or an AI coding assistant (Claude Code, Antigravity, GitHub Copilot Workspace, Cursor, etc.), you must follow these technical guardrails:

1. **Read [`CONTEXT.md`](CONTEXT.md) First:**
   - `CONTEXT.md` is the session entrypoint and must remain `<= 200` lines with its 10 required section headings intact (`RepoLintTests.The_context_file_exists_stays_short_and_keeps_its_structure`).
2. **Never Bypass Branch or Build Gates:**
   - Do not push directly to `main`, do not use `--no-verify`, do not weaken a test assertion to make CI green, and do not add `#pragma warning disable RS0030` without an explicit owner-approved entry in `RepoLintTests.BannedApiExceptions`.
3. **Native Imports (`[LibraryImport]`):**
   - Only `Jankscope.Platform` may contain `unsafe` code or `[LibraryImport]` declarations.
   - Every `[LibraryImport("dll", EntryPoint = "Name")]` must use literal strings, restrict search to `System32` (`[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`), and be listed in `ApprovedLibraryImports` in [`tests/Jankscope.Core.Tests/RepoLintTests.cs`](tests/Jankscope.Core.Tests/RepoLintTests.cs).
4. **WPF & XAML Reflection Avoidance:**
   - Do not attach event handlers inside a XAML `DataTemplate` or instantiate local assembly types in XAML where WPF would emit `GeneratedInternalTypeHelper.g.cs` (which calls `Activator.CreateInstance` and triggers `RS0030`). Wire events and custom controls (`TimelineView`) in C# code-behind.
5. **ASCII & UTF-8 Hygiene:**
   - All `.cs` and `.ps1` files must remain strictly ASCII (`< 0x80`). Never use Windows PowerShell 5.1 `Set-Content` without explicit encoding awareness.
   - All text files (`.md`, `.json`, `.yml`, etc.) are verified by `Text_files_are_valid_utf8_with_no_mojibake`.
6. **UI Snapshot Verification:**
   - If you modify any WPF view in `src/Jankscope.App`, regenerate and inspect the visual snapshots before opening a PR:
     ```powershell
     $env:JANKSCOPE_SNAPSHOT_DIR = "$PWD\docs\assets\screenshots"
     dotnet test tests\Jankscope.App.Tests\Jankscope.App.Tests.csproj -c Release
     ```
