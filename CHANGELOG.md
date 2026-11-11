# Changelog

All notable changes to **Jankscope** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Engineering Audit & Supply-Chain Hardening:**
  - Added automated tag-triggered release workflow (`.github/workflows/release.yml`) producing portable Windows x64 archives, `SHA256SUMS.txt`, and CycloneDX 1.5 JSON SBOM (`jankscope-<tag>-sbom.cdx.json`).
  - Added `.github/dependabot.yml` for automated weekly NuGet and GitHub Actions version/security checks and `.github/CODEOWNERS` for safety-critical path governance.
  - Enabled GitHub Private Vulnerability Reporting and GitHub Vulnerability Alerts on the repository.
  - Hardened `tools/fetch-third-party.ps1` with atomic temp-file downloads, explicit TLS 1.2/1.3 enforcement, and Authenticode signature validation (`Get-AuthenticodeSignature`).
- **Git Branching Strategy & CI Modernization:**
  - Implemented GitHub Flow branching governance with automated PR branch-name validation in `.github/workflows/ci.yml` (`<type>/<short-kebab-slug>` and `dependabot/*`), linear history enforcement, Squash/Rebase merge policies, and required status checks on `main`.
  - Upgraded GitHub Actions to native Node.js 24 releases (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`), added NuGet global package caching, and added `$env:GITHUB_STEP_SUMMARY` step-timing tables in `build.ps1`.

### Changed
- **Unified Redaction & Export Integrity:**
  - Unified `RedactionEngine.RedactText` onto `Redactor` (`RedactionContext`) so CLI (`jankscope incident`) and desktop (`jankscope-app`) exports share identical word-boundary rules for short names (`< 6` chars), IPv6, Email, MAC, IPv4, UNC host, and OneDrive organization path redaction.
  - Wired `ExportGuard.Verify` and atomic file writing (`Flush(flushToDisk: true)` + `File.Move`) into `jankscope incident --out <path>` and `SettingsStore.TrySave`.
  - Bounded and redacted `%LOCALAPPDATA%\Jankscope\error.log` (`256 KB` cap with `.old` rotation) and centralized product folder naming on `Branding.DataFolderName`.
  - Centralized `<Version>0.1.0</Version>` in `Directory.Build.props` so all assemblies (`Jankscope.Core`, `Jankscope.Platform`, `Jankscope.Frames`, `Jankscope.Cli`, `Jankscope.App`) and `CapabilityMatrix.ToolVersion` report a consistent version.

### Fixed
- **Export redaction of the person's own short names (independent Stage 1b review, finding 2):** a user or PC name of 2 to 5 characters glued to other characters in the note or the program name (for example `maria225`, `MariaPC`) is now removed, and the export guard catches it too. Names are matched by where the string sits: free text (`note`, `targetProcess`, ...) from 2 characters, this tool's own values from 4, property names from 6 (so the fixed schema is not renamed); names in Chinese, Japanese, Korean and Thai are matched as substrings; names are compared after Unicode normalisation. IPv4 and MAC addresses are found when letters, digits or underscores touch them, and in the `aabb.ccdd.eeff` and 12-hex-digit notations. The welcome screen and the export dialog no longer promise that anything that slips through is blocked: only what Jankscope knows about is removed, so the note must be read.
- **Status documents corrected after the independent Stage 1b review (finding 1):** `STATUS.md`, `CONTEXT.md`, `HANDOFF.md` and `TESTING.md` no longer call the authors' own review (`docs/reviews/stage-1b.md`) independent, and they list the findings of the independent review (`docs/reviews/stage-1b-independent.md`) that are still open. Test totals now come from CI run `41867073761` (2,548 tests: 2,544 passed, 4 skipped). `ROADMAP.md` cites ADR 0002 (not 0008) for the out-of-process architecture, and `ROADMAP.md` and `RELEASE_READINESS.md` quote the overhead deltas that `docs/benchmarks/overhead-v1.md` records. No code changed.
- **Independent Stage 1b review, findings 3 to 6 (PR #10):** a live capture of `jankscope-harness.exe` is labelled Simulated from the program name instead of being exported as a measurement (HC-9); out-of-order frames and jumps of the capture clock are counted, shown and exported, and one wrong timestamp can no longer silence a capture; the key-press time uncertainty is given only from 20 observations (`null` otherwise: unknown, not zero) and is always described as frame-delivery jitter only; a managed ban, a pin on the six keys the timeline reads and a XAML scan refuse code that reads the keyboard (HC-4).
- **Process-name and lock-file checks no longer depend on the host OS (PR #10):** `ValidateProcessName` and the lock-file escape check took their rules from `Path`, which treats `\` as a separator and `*?"<>|:` as invalid only on Windows, so hostile Windows-style names passed elsewhere. The rules are now stated; Windows behaviour is unchanged.
- **Tests for safety rules no test pinned (independent Stage 1b review, finding 17, PR #11):** hotkey key range, redaction thresholds and ordering, the profile folder name, the 10 s look-back, non-finite mark times, the pre-roll covering the clock uncertainty, weakest-provenance-wins, a new capture run starting a new clock epoch and `MOD_NOREPEAT`, each shown to fail under a mutant. Two survivors (L2, L5) remain.
- **Shipped files are never taken from the root of the drive (independent Stage 1b review, finding 15):** the search for the catalogs and for the capture tool's lock file stopped only at the drive root, where a standard user can usually create folders. It now stops below it.
- **Settings file no longer contains computed hotkey values (finding 14).**
- **The live refresh stops while the window is hidden; Quit always ends the program; candidate list items have a readable accessibility name (independent Stage 1b review, findings 8, 11 and 12).** Tests now pin that only the hotkey message is acted on and that a new capture starts with an empty clock.
- **The key-press time is taken when the press is first handled, not after the hop to a worker thread (finding 10).**
- **Limitations stated as they are (findings 9 and 16):** a crash leaves the capture tool running until the next start (L23); the real-binary tests are still timing-sensitive (L27); the 200 MB memory budget of the spec is not shown to be met (L30).

---

## [0.1.0] - 2026-11-04

### Added
- **Stage 1b — Interactive Slice (`Jankscope.App`, `Jankscope.Frames`, `Jankscope.Core`, `Jankscope.Platform`):**
  - WPF desktop application (`jankscope-app.exe`) with Capture, Stutter Investigator, Settings, About & Privacy pages, Welcome dialog, Export Preview dialog, and system tray integration (`TrayIcon`).
  - Custom `TimelineView` rendering min/median/p95/max pixel-bucket frame-time bands that preserve single-frame spikes at any zoom level.
  - Global hotkey registration (`RegisterHotKey` / `WM_HOTKEY` via `user32.dll`, zero keyboard hooks) for `Ctrl+Alt+F10` (Mark Stutter) and `Ctrl+Alt+F11` (Mark Input Stall), plus non-blocking synthesized PCM WAV audio cues (`AudioCue`).
  - `CaptureClockMap`, `CandidateAnchorFinder`, `FrameSeries`, and `IncidentRecorder` with epoch-isolated pre-roll and post-roll windows.
  - Two-layer fail-closed export redaction pipeline (`Redactor` + independent `ExportGuard` + `ExportPipeline.WriteAtomic`).
- **Stage 1a — Headless Core (`Jankscope.Frames`, `Jankscope.Platform`, `Jankscope.Cli`, `Jankscope.Harness`):**
  - Lock-free single-writer `RingBuffer<T>`, streaming/batch `StutterDetector`, and `GroundTruthMatcher`.
  - Culture-invariant streaming `PresentMonCsvParser` and handle-verified `PresentMonRunner` (`FileHashVerifier` with `FileShare.Read` + reparse-point rejection, `PresentMonArguments` allowlist rejecting `--restart_as_admin`).
  - `OrphanSessionCleaner` cleaning stale `Jankscope-*` ETW sessions via `advapi32.dll!ControlTraceW` without opening foreign process handles.
  - CPU topology (`GetLogicalProcessorInformationEx`), GPU adapter discovery by LUID (`Vortice.DXGI`), CCD display topology (`QueryDisplayConfig`), PDH performance counter sampler, and catalog-driven VM/anti-cheat/game-session probes.
  - Direct3D 11 flip-model synthetic stutter harness (`Jankscope.Harness`) with QPC frame pacing and WARP fallback.
  - 10-run interleaved A-B-A-B self-overhead benchmark (`tools/dev/Measure-Overhead.ps1`, `docs/benchmarks/overhead-v1.md`) proving `+0.009 ms` median present-error delta (`< 0.200 ms` budget).
- **Stage 0 — Bootstrap & Static Safety Architecture:**
  - `.NET 10` (`10.0.402`) solution (`Jankscope.slnx`), one-command build and verification pipeline (`build.ps1`), compile-time `BannedSymbols.txt` (`RS0030`, 106 proven entries), lexer-based `CSharpScanner` + `RepoLintTests`, and ADRs `0001`–`0008`.
