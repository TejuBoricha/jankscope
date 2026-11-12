# Architecture (Stage 1a + Stage 1b Verified)

Full requirements: [`MASTER_PROMPT_v4.md`](../MASTER_PROMPT_v4.md) §6. Architectural Decision Records: [`docs/adr/`](adr/).

---

## 1. Solution & Assembly Topology

| Project | Target | Role | Unsafe Code / Native Imports |
| :--- | :--- | :--- | :--- |
| [`src/Jankscope.Core`](../src/Jankscope.Core) | `net10.0` | Pure, portable domain logic: capability matrix, provenance state machine (`HC-9`), frame statistics, synthetic workload generator (`SplitMix64`), anti-cheat decision table (`HC-1`), ETW session classifier, catalog schema validator, global hotkey chord parser, and two-layer export redaction (`Redactor`, `ExportGuard`, `ExportPipeline`). | **None** |
| [`src/Jankscope.Platform`](../src/Jankscope.Platform) | `net10.0-windows` | Windows hardware, security, and OS probes behind `IProbe`; `OrphanSessionCleaner`; `PdhSampler`; `HotkeyRegistrar` (`RegisterHotKey` / `UnregisterHotKey`); and `TitleBar` (`DwmSetWindowAttribute`: the dark title bar of this program's own windows). Every probe runs with timeout and exception isolation (`TaskCreationOptions.LongRunning`). | **Only assembly with `AllowUnsafeBlocks=true`** and the 27 test-pinned `[LibraryImport]` entry points (`System32`-only search path). |
| [`src/Jankscope.Frames`](../src/Jankscope.Frames) | `net10.0` | Frame pipeline: lock-free single-writer `RingBuffer<T>`, streaming/batch `StutterDetector`, `GroundTruthMatcher`, culture-invariant `PresentMonCsvParser`, handle-verified `PresentMonRunner` (`FileHashVerifier` + `PresentMonArguments`), `CaptureClockMap`, `CandidateAnchorFinder`, `IncidentRecorder`, and `IncidentBundleBuilder`. | **None** (owns the single reviewed `PresentMonProcessLauncher` exception to `RS0030`). |
| [`src/Jankscope.Cli`](../src/Jankscope.Cli) | `net10.0-windows` | Command-line tool (`jankscope probe`, `jankscope incident`, `jankscope version`). Ships `catalogs/*.json` alongside the binary. | **None** |
| [`src/Jankscope.App`](../src/Jankscope.App) | `net10.0-windows` | WPF desktop application (`jankscope-app.exe`): Capture, Stutter Investigator, Settings, and About pages; min/median/p95/max pixel-bucket `TimelineView`; `ExportWindow` with SHA-256 preview verification; `WelcomeWindow`; synthesized PCM WAV `AudioCue`; and system-tray host (`TrayIcon`). | **None** |
| [`tools/Jankscope.Harness`](../tools/Jankscope.Harness) | `net10.0-windows` | Direct3D 11 flip-model synthetic stutter harness with QPC frame pacing, hardware + WARP fallback, and PresentMon-compatible CSV/JSON output tagged `Simulated` (`ADR 0006`). | **None** |
| [`tools/Jankscope.FakePresentMon`](../tools/Jankscope.FakePresentMon) | `net10.0` | Deterministic test double for the PresentMon 2.6.0 console executable used by supervision, watchdog, and session-cleanup tests. | **None** |
| [`catalogs/`](../catalogs) | JSON data | Schema-validated data catalogs (`anticheat.json`, `protected_titles.json`, `vendor_terms.json`, `vm_markers.json`). | **Data only** |

---

## 2. End-to-End Data & Control Flows

### A. Capability Matrix & Anti-Cheat Gating (`jankscope probe`)
```
catalogs/*.json --validate/load (fail-closed)--> CatalogSet
IProbe[] (os, privilege, etw, security, cpu, gpu, display, environment, pdh, managed, anticheat, game.session, planned)
   --> CapabilityMatrixBuilder (isolated LongRunning worker + per-probe timeout)
   --> DerivedCapabilities (frame.capture, mode.optimize)
   --> CapabilityMatrix (every row carries Tier, Availability, Provenance, Source, and Reason)
```

### B. Live Frame Capture, User Marking & Guarded Export (`jankscope-app`)
```
PresentMon-2.6.0-x64.exe (pinned in third_party/PresentMon/presentmon.lock.json)
   --> FileHashVerifier.Open (FileShare.Read + reparse-point check + size & SHA-256 verification on open handle)
   --> PresentMonArguments.EnsureSafe (ArgumentList allowlist; --restart_as_admin unconditionally rejected)
   --> PresentMonRunner (bounded drop-oldest Channel + watchdog + exponential backoff + ETW session cleanup)
   --> CaptureSession (dedicated background pump thread; filters PresentMode == "Other")
   --> RingBuffer<FrameSample> + CaptureClockMap (5 ms restart discontinuity threshold)
   --> HotkeyHost (RegisterHotKey WM_HOTKEY) -> IncidentRecorder (pre-roll + post-roll window)
   --> CandidateAnchorFinder (stutter events, collector gaps, weak spikes; never auto-selects)
   --> IncidentBundleBuilder -> ExportPipeline.Prepare (Redactor + independent ExportGuard) -> WriteAtomic
```

---

## 3. Hard Constraints (`HC-1` .. `HC-13`) & Compile-Time Enforcement

1. **Dual-Layer Banned-API Enforcement (`HC-1`, `HC-2`, `HC-5`, `HC-7`, `HC-8`):**
   - **Symbol Layer:** `Microsoft.CodeAnalysis.BannedApiAnalyzers` (`RS0030`) with 106 entries in [`BannedSymbols.txt`](../BannedSymbols.txt), verified on every build by [`tools/dev/verify-banned-symbols.ps1`](../tools/dev/verify-banned-symbols.ps1) and [`tools/dev/verify-analyzer-wiring.ps1`](../tools/dev/verify-analyzer-wiring.ps1).
   - **Lexer/AST Layer:** [`CSharpScanner`](../tests/Jankscope.Core.Tests/CSharpScanner.cs) + [`RepoLintTests`](../tests/Jankscope.Core.Tests/RepoLintTests.cs) enforce ASCII-only identifiers, exact `[LibraryImport]` literal signatures against a 26-entry allowlist, `DllImportSearchPath.System32`, and zero hardcoded vendor names outside `catalogs/`.
2. **Provenance Integrity (`HC-9`):** Every datum carries `DataProvenance` (`Measured`, `Inferred`, `Assumed`, `Simulated`, `Fixture`, `Unknown`); combining values propagates the weakest provenance.
3. **Fail-Closed Export Privacy:** `Redactor` scrubs sensitive tokens and `ExportGuard` independently inspects raw text, URL-decoded strings, and JSON-unescaped strings before any byte is written to disk.

---

## 4. Planned Stages (Not Yet Built)
- **Stage 2:** Raw Input & network latency collectors (gated on Stage 1 owner live-game validation per [`docs/VALIDATION_1.md`](VALIDATION_1.md)).
- **Stage 3:** Expanded ETW ISR/DPC attribution, SQLite persistence, and Engine/UI/Elevated-Helper named-pipe IPC (`ADR 0002`).
- **Stage 4:** Controlled optimization journal with automatic rollback verification.
- **Stage 5:** Code signing (`ADR 0007`), installer packaging, and release go/no-go gate.
