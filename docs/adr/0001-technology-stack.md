# ADR 0001 — Technology stack

**Status:** Accepted (2026-10-16) · **Spec:** §6.6

## Context
A Windows-only desktop diagnostics tool that must run for standard users on any gaming PC, with no .NET runtime pre-installed, low overhead, and a small trusted elevated helper. The reference bench has no Visual Studio, MSVC or Windows SDK.

## Decision
- **C# on .NET 10 (LTS)**: `net10.0` for pure logic (`Jankscope.Core`), `net10.0-windows` for Windows-specific code. A build error blocks any project targeting .NET 8/9 (end of support 2026-11-10, FACTS F2).
- **Self-contained publish** for releases, so end users need no runtime.
- **WPF** for the UI (Stage 1b) — mature, built-in tray via WinForms `NotifyIcon`, hotkeys via `HwndSource`, no packaging dependency, easy elevation behaviour.
- **xUnit v2** (2.9.3) for tests; central package management with exact versions; every package recorded in `docs/THIRD_PARTY.md` and enforced by a test.
- **P/Invoke through `LibraryImport`** (source-generated, trim-friendly) for the few Win32 calls; no C++/CLI, no native helper code.
- SQLite (Stage 1a), charts (ScottPlot.WPF candidate) and TraceEvent are adopted only when needed, after a license and maintenance check.

## Alternatives rejected
| Option | Why not |
|---|---|
| WinUI 3 | Reported gaps: no built-in tray icon, no single-file publish, elevation-launch problem on 24H2 (FACTS F9, still to verify). WPF costs nothing we need. |
| WinForms only | Poor DPI and charting story for the planned timeline UI. |
| Electron / web UI | Memory and CPU overhead conflict with the observer-effect budget (§14); a large attack surface for a tool that must be trusted. |
| C++ | No MSVC/Windows SDK on the bench; memory-safety cost for no benefit; ADR 0006 keeps the harness in C# too. |
| .NET 8/9 | End of support 2026-11-10. |
| xUnit v3 / NUnit / MSTest | No decisive advantage today; revisit if v3 tooling matures. |

## Consequences
- The Core library stays portable and testable on any machine; Windows-only code is isolated in `Jankscope.Platform`.
- Unsafe code is permitted solely for source-generated P/Invoke; manual memory handling is confined to `EtwNative` and reviewed.

## Verification
`./build.ps1` (0 warnings), `RepoLintTests.Every_project_targets_net10_only`, `Package_references_carry_no_inline_version_and_are_recorded_in_third_party_docs`.
