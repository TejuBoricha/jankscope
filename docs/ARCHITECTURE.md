# Architecture (Stage 0 snapshot)

Full requirements: `MASTER_PROMPT_v4.md` §6. Decisions: `docs/adr/`.

## Projects
| Project | Target | Role |
|---|---|---|
| `src/Jankscope.Core` | net10.0 | Portable, testable logic: capability model, provenance rules, frame statistics, synthetic workload generator, anti-cheat state/decision logic, ETW session classification, catalog schema + loader. No Windows-only code. |
| `src/Jankscope.Platform` | net10.0-windows | Windows probes behind a small `IProbe` contract; the only place with P/Invoke (`Native.cs`, `EtwNative`) and WMI. Failures are isolated per probe and become explicit "not available (reason)" rows. |
| `src/Jankscope.Cli` | net10.0-windows | `jankscope probe [--json]`, read-only. Ships the catalogs next to the executable. |
| `tools/Jankscope.Harness` | net10.0 | Synthetic ground-truth generator (renderer arrives in Stage 1a). |
| `catalogs/` | data | Data-only knowledge (anti-cheat, protected titles, forbidden vendor terms). |
| `tests/*` | | Unit, lint, platform behaviour. |

## Flow of the probe
```
catalogs/*.json --validate/load--> CatalogSet
IProbe[] (os, privilege, etw, security, hardware, managed, anticheat, planned)
   --Run() each, isolated--> CapabilityEntry rows (+ ProbeHealth)
   --DerivedCapabilities--> frame.capture, mode.optimize
   --> CapabilityMatrix --> table or JSON
```
Every row carries its **source** and, when not available, a **reason**; unknown states are reported as unknown, never as "off" or zero.

## Planned (not built)
Engine / UI / elevated-helper processes and the hardened IPC (ADR 0002) · collectors and ring buffers · SQLite storage and incident bundles · diagnosis engine and rules · optimization journal and rollback · WPF UI. See `docs/STATUS.md` for the stage plan.

## Rules that shape the code
HC-1 (anti-cheat safety) → `AntiCheatSafety`, ADR 0005 · HC-4 (input privacy) → Stage 2 · HC-8 (security report-only) → `SecurityPostureProbe` reads, never writes · HC-9 (provenance) → `DataProvenance` + export guard · HC-11 (managed/unknown ⇒ Recommend-only) → `mode.optimize` · HC-12 (no vendor logic) → catalogs + lint.
