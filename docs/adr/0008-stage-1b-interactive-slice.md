# ADR 0008 - Stage 1b interactive slice: in-process engine host, hotkey marker, candidate anchors, redacted export

**Status:** Accepted (2026-11-04; owner delegated defaults) - amends ADR 0002 for Stage 1b only - **Spec:** section 7.3, section 15, section 16, HC-1, HC-4, HC-5, HC-9

## Context
Stage 1b must put the Stage 1a core in front of a person: capture a game, let the person press a hotkey when it stutters, show a timeline, and export a redacted incident they can read before it leaves the screen. ADR 0002 describes the final three-process model (engine, UI, elevated helper). Implementing the user-to-engine named pipe now would cost a hardened pipe server and client (the client type is banned in product code on purpose) for no Stage 1b benefit: nothing in 1b needs elevation, and a second process would only move the same frames across a pipe.

## Decision
1. **In-process engine host, behind small interfaces.** The WPF app (`src/Jankscope.App`) hosts a `CaptureSession` (Jankscope.Frames) in its own process. PresentMon still runs as its own child process, the ring buffer is lock-free and single-writer, and the UI thread only reads snapshots, so rendering is never on the capture path (spec 6.1). The app is **tray-resident**: closing the window hides it and capture continues until the person chooses Quit from the tray. The pipe split stays planned for Stage 3 (ADR 0002 is unchanged for that stage); nothing in `Jankscope.Frames` knows about WPF, so the split is a transport change.
2. **Hotkeys: `RegisterHotKey` only (HC-4).** `Jankscope.Platform` wraps `RegisterHotKey` / `UnregisterHotKey` and maps the failure (chord already owned by another program, invalid key, other) to a typed result. WM_HOTKEY is received by a WPF message-only `HwndSource`. No keyboard hook, no key codes read, no per-key timing. A failed registration is shown (tray state and the Settings page) with the reason, and the tray menu always offers **Mark stutter** and **Mark input stall** as the fallback.
3. **The key press is not the event.** On a mark the engine searches the preceding 10 s for candidate anchors (largest frame spike, stutter events, collector gaps, clock gaps) and shows them ranked. The mark has **no anchor until the person picks one**, or explicitly picks "the time I pressed the key". Mapping the key press onto the capture timeline uses a one-way-delay estimator (`CaptureClockMap`) and carries an **uncertainty in milliseconds** that the timeline draws as a band (spec 6.2).
4. **Timeline UI without a charting package.** A custom WPF element draws min/max-bucketed frame times, events, marks, anchors and uncertainty. No new NuGet package is added (the ScottPlot candidate of ADR 0001 is not needed and would need the owner's yes).
5. **Redacted export with preview, fail closed.** Export is built in memory, redacted by `RedactionEngine` (username, hostname, SSID, BSSID, MAC, IP, device serial, command line, free-text note), previewed in a dialog that shows the exact text that will be written and what was replaced per category, then verified against **canary strings** (every known sensitive value of this machine plus planted test values). If any canary survives, nothing is written. Data whose provenance is not Measured or Inferred is labelled as such in the file (HC-9); the harness's data is always labelled Simulated. The harness writes that label into its own log, but PresentMon never prints it, so a live capture of the harness is labelled from the program name instead (`CaptureProvenance`: a capture of `jankscope-harness.exe` is Simulated, every other program is Measured).
6. **Egress: none.** Export writes a file the person chose. The app opens no network connection (HC-5) and adds no telemetry.

## Alternatives rejected
| Option | Why not |
|---|---|
| Full engine/UI pipe now | Needs a banned client type and a hardened pipe for no 1b benefit; delays the validation stop. Planned for Stage 3 with the helper. |
| Low-level keyboard hook for the marker | Forbidden by HC-1 and HC-4 (looks like a keylogger to antivirus). |
| ScottPlot / WPF charting package | Not needed for one line series; avoids a new dependency and an owner approval. |
| Align the mark to the key press silently | Human reaction delay is 150-400 ms or more; spec 7.3 forbids silent alignment. |
| Export first, redact in the viewer | A leak is one click away; redaction must be a property of the bytes written. |

## Consequences
- ADR 0002's "UI closed during play" requirement is met by tray residency only while the app runs; a crash of the app ends capture. Recorded in `docs/LIMITATIONS.md`.
- The in-process host is replaced, not rewritten, at Stage 3.
- The hotkey, the audible cue and the tray state are the only user feedback during play (no overlay, HC-1).

## Verification
Test plan `docs/test-plans/stage-1b.md`; owner validation `docs/VALIDATION_1.md`.
