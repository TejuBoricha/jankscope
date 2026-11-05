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
