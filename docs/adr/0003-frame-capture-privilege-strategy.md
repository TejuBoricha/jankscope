# ADR 0003 — Frame-capture privilege strategy

**Status:** Accepted (design); implementation and the PresentMon download are Stage 1a and need the owner's fresh yes · **Spec:** §6.6 (mandatory ADR), HC-1, HC-7

## Context
Frame-level capture needs a real-time ETW session. That requires either membership in **Performance Log Users** or admin rights (FACTS F5, F14). The product must work for standard users and must be careful around anti-cheat (HC-1): in game sessions the elevated helper, kernel ETW, WPR and USB tracing are suppressed, while unelevated user-mode ETW consumers (the PresentMon console) are on the HC-1 allow list.

## Options evaluated
| Option | Verdict |
|---|---|
| (a) Capture inside the per-session elevated helper | Accepted **only outside game sessions** (harness, benchmarks, idle), or in a game after the user overrides per game. Suppressed by default in game sessions (HC-1). |
| (b) Unelevated capture when the user can already open a real-time ETW session | Accepted as the **in-game path**. Detected by actually creating a uniquely named session (not by reading group membership); the app never edits groups. |
| (c) PresentMon's own service / C API | Rejected: no official .NET wrapper, binary-compatible only with the DLL its own service installs, and another product's service may already be running (never contact it). |
| (d) Bundle a pinned PresentMon build vs download at run time | **Bundle** (build time, hash-verified before every launch). A runtime download is rejected by HC-7. Fetching the binary for bundling is a third-party download and needs the owner's explicit yes at that time. |
| Re-implement PresentData over TraceEvent | Rejected: large, fragile, and 2.x metrics are not comparable with 1.x logs. |

## Decision
Use the **PresentMon console executable** (MIT, FACTS F11), pinned to an exact version with its SHA-256 recorded, launched by the Engine with a unique session name (`Jankscope-<pid>`), `--output_stdout`, `--process_name`, `--terminate_on_proc_exit` and a watchdog plus one-click stop. `--stop_existing_session` / `--terminate_existing_session` only for an orphan carrying our own prefix. Parsers read CSV columns by name and ignore unknown ones.

## Coverage matrix (also in `CAPABILITIES.md` and the UI)
| Situation | Frame pacing | Elevated / kernel collectors |
|---|---|---|
| Game session, real-time ETW access works | **Available** (unelevated) | Suppressed unless the user overrides per game |
| Game session, no access | **Unavailable** — UI names the one-time user action (join Performance Log Users, `INSTALL.md`); other diagnostics continue | Suppressed |
| No game session | Available via helper or unelevated | Allowed (UAC, user-initiated) |
| Anti-cheat resident only | As above | Allowed, with a notice |
| VM / managed / unknown | Per §3.5 (Recommend-only) | Not offered |

`jankscope probe` already derives `frame.capture` from the real access test (`DerivedCapabilities`, tested).

## Consequences
Most users on protected titles see "unavailable" until they complete the one-time group step. This is a documented limitation, not a bug, and the product never works around it. PresentMon release assets, the release year and hashes are still open (FACTS F12).

## Verification
Stage 0: access test and derivation are implemented and tested on the bench (unelevated session start/stop with cleanup verified). Stage 1a: harness-based detection ±1 frame.
