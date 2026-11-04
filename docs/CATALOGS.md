# Knowledge catalogs

Spec §3.4, HC-12. Catalogs are **data only** (JSON), shipped next to the executable, validated on load (an invalid catalog stops loading — fail closed), and extensible by users and contributors. Product code never names vendors or models; behaviour that depends on them comes from these files.

| File | Purpose | Used by |
|---|---|---|
| `catalogs/anticheat.json` | Anti-cheat services/drivers and helper processes → detects a resident anti-cheat (HC-1 state 1) | `AntiCheatProbe` |
| `catalogs/protected_titles.json` | Executables of commonly protected games → HC-1 state 3 | `AntiCheatProbe` |
| `catalogs/vm_markers.json` | Firmware/WMI strings that identify a virtual machine (catalog-driven; no vendor names in code) | `VirtualMachineProbe` |
| `catalogs/vendor_terms.json` | Vendor, OEM, peripheral and anti-cheat names forbidden in product code | `RepoLintTests` |

## Schema (version 1)
Top level: `schemaVersion` (must be 1), `catalog` (kind), `description`, `entries`. Each entry requires:
- `id` — `^[a-z0-9_]+$`, unique in the file
- `displayName`
- `source` — a URL, or `observed:<where>` for something seen on a machine
- `lastReviewed` — ISO date `yyyy-MM-dd`
- `verification` — one of `observed` (seen on a real machine), `documented` (stated in the project's own policy or the vendor's documentation), `community` (widely reported, not verified here), `unverified`
- at least one kind-specific list of plain names: `serviceNames` / `processNames` (anti-cheat), `processNames` (protected titles), `terms` (vendor terms)
- optional `notes`

Names may contain only letters, digits, `.`, `_`, `-` and space — no paths, wildcards or shell characters. Any other property is rejected, so a catalog cannot smuggle commands or scripts.

## Vendor-adapter exception
Optional vendor API adapters (NVML, ADLX, IGCL) will live in assemblies whose path contains `VendorAdapters`. The lint skips those, and only those; they may name their own API and nothing else, and expose only vendor-neutral interfaces. No such assembly exists yet.

## Updating
Add or change an entry, keep `source` and `lastReviewed` honest, run `./build.ps1`. Updates after release will be signed data packs (P2); v1 has no remote catalog download.
