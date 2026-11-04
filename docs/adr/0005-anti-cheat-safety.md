# ADR 0005 — Context-aware anti-cheat safety

**Status:** Accepted (2026-10-16) · **Spec:** HC-1, §2.2 #8 · **Code:** `Jankscope.Core/AntiCheatSafety.cs`, `Jankscope.Platform/Probes/EtwAndAntiCheatProbes.cs` (AntiCheatProbe)

## Context
v3 suppressed elevated tooling whenever any anti-cheat service was running. On the reference bench a boot-start kernel driver (`vgk`, observed `Start=1`) is always loaded, so Tier C would have been permanently off. The owner also asked that the product never turn off things that "might stop something important": it must be smart, not blanket.

## Decision
Classify the machine into four states and suppress only the app's **own** restricted collectors:

| State | Meaning | Suppressed |
|---|---|---|
| 0 Idle | no game session, no protected process, nothing resident | nothing |
| 1 ResidentOnly | an anti-cheat service/driver loads at boot (service `Start` 0–2) or its helper process runs, no game | nothing (shown as context) |
| 2 GameSession | a consented-list game or sustained full-screen presenter is active | elevated helper, kernel ETW, WPR, USB tracing, Deep profile, foreign-process handles |
| 3 ProtectedTitle | a catalogued protected-title process is running | same as 2 |

Never suppressed: unelevated user-mode ETW (PresentMon console), PDH, WMI/CIM, event logs, Raw Input timing, network probes. A per-game user override re-enables restricted collectors except foreign-process handles beyond limited query. The app never stops, pauses or modifies any other program or service.

Knowledge (service names, process names, protected titles) lives in `catalogs/*.json` (HC-12), each entry with a source, review date and verification level (`observed` / `documented` / `community` / `unverified`).

## Alternatives rejected
Blanket suppression whenever anti-cheat is installed (disables Tier C on many machines); no suppression at all (risks being mistaken for tampering); terminating or throttling other software (unsafe and out of scope).

## Consequences
Game-session detection (state 2) is not implemented until Stage 1a; the probe says so and cannot report state 2 today. We cannot guarantee that no anti-cheat vendor objects to ETW consumers; the product states only what it does not do (HC-1 wording).

## Verification
`AntiCheatSafetyTests` (decision table, allow list, override rules), `AntiCheatProbe` tests with synthetic catalogs, real probe on the bench: Vanguard resident, Easy Anti-Cheat installed on demand, state ResidentOnly, nothing suppressed.
