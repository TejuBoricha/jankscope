# Third-party components

Policy (spec HC-10, §4.2): record license, maintenance status, redistribution terms and limits **before** adopting anything. Allowed licenses for linked code: MIT, BSD, Apache-2.0, ISC, MPL-2.0 (unmodified or file-level). Not allowed: GPL, AGPL, SSPL, non-commercial or unclear terms. No third-party kernel drivers (HC-2). An SBOM (CycloneDX) is generated per release build in Stage 5; this file is the hand-maintained register until then.

Licenses below were read from each package's own `.nuspec` after restore on 2026-10-16 (not from memory).

**Owner delegation 2026-10-17:** the Vortice.* packages and their transitive packages (including the pre-release `SharpGen.Runtime` and `SharpGen.Runtime.COM` 2.4.2-beta) were restored by the Stage 1a work packages as ADR 0006 planned. The owner delegated the decision to the maintainer assistant ("it is up to you") and they are accepted; re-check at every update and when a stable SharpGen release appears.

## Packages in use (Stage 0)

| Package | Version | License (from nuspec) | Used by | Ships in product? | Notes |
|---|---|---|---|---|---|
| System.Management | 10.0.14 | MIT | Jankscope.Platform | Yes | WMI queries (chassis type). Microsoft-maintained. |
| System.CodeDom | 10.0.14 | MIT | (transitive of System.Management) | Yes | |
| Vortice.Direct3D11 | 3.8.3 | MIT (nuspec: `<license type="expression">MIT</license>`) | Jankscope.Harness (`tools/`) | No (development and test tool; not part of the product installer) | Direct3D 11 binding for the synthetic harness (ADR 0006). Author Amer Koleci; repository github.com/amerkoleci/Vortice.Windows. Transitive packages are the same as for Vortice.DXGI (rows below). |
| Vortice.DXGI | 3.8.3 | MIT (nuspec: `<license type="expression">MIT</license>`) | Jankscope.Platform (GPU adapter list by LUID) | Yes | DXGI bindings; author Amer Koleci; repository github.com/amerkoleci/Vortice.Windows (nuspec commit 9e609cb). Used only through `IDXGIFactory1.EnumAdapters1` and `Description1`: no device is created and no output is enumerated, so a sleeping GPU is not woken. Chosen because hand-written COM is banned by `BannedSymbols.txt` (ADR 0006). Added with Stage 1a package C. |
| Vortice.DirectX | 3.8.3 | MIT | (transitive of Vortice.DXGI) | Yes | Same repository and commit. |
| Vortice.Mathematics | 2.1.0 | MIT | (transitive of Vortice.DirectX) | Yes | Repository github.com/amerkoleci/Vortice.Mathematics. ADR 0006 named 2.1.1; the restored dependency is 2.1.0. |
| SharpGen.Runtime | 2.4.2-beta | MIT | (transitive of Vortice.DXGI / Vortice.DirectX) | Yes | Pre-release version label. Repository github.com/SharpGenTools/SharpGenTools. Its `build/SharpGen.Runtime.props` only registers a mapping file as an item; it adds no build-time target or code. |
| SharpGen.Runtime.COM | 2.4.2-beta | MIT | (transitive of Vortice.DirectX) | Yes | Same repository; `build/*.props` only register mapping files. |
| Microsoft.CodeAnalysis.BannedApiAnalyzers | 5.6.0 | MIT (nuspec: `<license type="expression">MIT</license>`) | every product project in `src/` and `tools/` via `Directory.Build.targets` (analyzer; build-time only, `PrivateAssets=all`) | No | Authors/owners Microsoft; repository github.com/dotnet/roslyn; no package dependencies; signed package. Published 2026-07-02. Enforces `BannedSymbols.txt` as RS0030. **Adopted 2026-10-16 with the owner's explicit yes.** Method entries need full overload signatures; `tools/dev/verify-banned-symbols.ps1` (run by `build.ps1`) proves every entry fires. |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | tests | No | |
| Microsoft.TestPlatform.ObjectModel | 18.11.0 | MIT | tests (transitive) | No | |
| Microsoft.TestPlatform.TestHost | 18.11.0 | MIT | tests (transitive) | No | |
| Microsoft.CodeCoverage | 18.11.0 | MIT | tests (transitive) | No | |
| xunit | 2.9.3 | Apache-2.0 | tests | No | v2 line chosen over v3 for ecosystem stability; revisit in an ADR if needed. |
| xunit.core / xunit.assert / xunit.extensibility.core / xunit.extensibility.execution | 2.9.3 | Apache-2.0 | tests (transitive) | No | |
| xunit.analyzers | 1.18.0 | Apache-2.0 | tests (transitive) | No | |
| xunit.abstractions | 2.0.3 | license URL only (xunit's repository license) | tests (transitive) | No | The nuspec declares only a URL; the xunit project is Apache-2.0. Re-check when updating. |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 | tests | No | |

## Tools used to build (not redistributed)

| Tool | Version | License / source | Notes |
|---|---|---|---|
| .NET SDK | 10.0.402 | Microsoft; installed from the official `winget` package `Microsoft.DotNet.SDK.10` (installer SHA-256 `930a215d2bcb3154c1898bd0fb9083a5a7755e8ccc26985309b144556557851a`, verified by winget) | Installed 2026-10-16 with the owner's approval; .NET 10 is LTS. |

## Planned (not adopted yet — each needs a license and maintenance check at adoption)

| Component | Intended use | Known so far | Open items |
|---|---|---|---|
| PresentMon (console) | Frame capture, bundled at build time (ADR 0003) | MIT, copyright Intel Corporation. v2.6.0, published 2026-09-21: `PresentMon-2.6.0-x64.exe`, 980,320 bytes, GitHub-published SHA-256 `b2a706bc…88f1af` (full value in FACTS F12). v2.5.0's download was removed upstream. | **Downloaded 2026-10-16 with the owner's explicit yes.** Our own SHA-256 matches the published digest; Authenticode signature valid, signer Intel Corporation. Pinned in `third_party/PresentMon/presentmon.lock.json`; the binary is git-ignored; `tools/fetch-third-party.ps1` verifies it. The runner must verify the hash before every launch and must never pass `--restart_as_admin`. |
| TraceEvent | ETW consumption, if needed beyond PresentMon | MIT (per spec; not yet re-verified) | Pin version; read release notes. |
| ScottPlot.WPF | Charts | MIT per spec; not yet re-verified | .NET 10 compatibility. |
| SQLite provider | Local storage | Not yet chosen | |
| Vendor GPU APIs (NVML, ADLX, IGCL) | Optional adapters | NVML: no redistribution terms found — P/Invoke the driver-installed library, never ship it. ADLX: own SDK license — read before shipping. IGCL: license not confirmed. | |

## Rejected

| Component | Reason |
|---|---|
| WinRing0 / PawnIO-style sensor drivers | HC-2: third-party kernel drivers are unreliable under HVCI and anti-cheat, and WinRing0 is on Microsoft's driver blocklist. |
