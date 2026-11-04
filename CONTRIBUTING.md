# Contributing to Jankscope

Thank you for your interest in contributing to **Jankscope** — a free, open-source (MIT), zero-telemetry diagnostic oscilloscope for Windows gaming PCs.

Because Jankscope runs alongside games protected by kernel-level anti-cheat systems and handles local diagnostic data, **every contribution is governed by strict compile-time, lexer-level, and runtime safety proofs**. Please read these guidelines carefully before opening a pull request.

---

## 1. Non-Negotiable Hard Constraints (`HC-1` .. `HC-13`)

Every line of product code (`src/` and `tools/`, excluding `tests/`) must obey the hard constraints in [`MASTER_PROMPT_v4.md`](MASTER_PROMPT_v4.md) section 5:

| Constraint | Rule | How It Is Enforced |
| :--- | :--- | :--- |
| **`HC-1` Anti-Cheat Safety** | Never inject DLLs, install hooks (`SetWindowsHookEx`), draw in-game overlays, attach debuggers, or open foreign process handles (`OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`). | `BannedSymbols.txt` (`RS0030`) + `RepoLintTests` (`CSharpScanner` lexer scan) |
| **`HC-2` Kernel & Driver Safety** | Never ship, load, or extract a kernel driver (`CreateService`, `NtLoadDriver`), and never bundle vulnerable third-party hardware drivers. | `BannedSymbols.txt` + `ApprovedLibraryImports` exact-set gate |
| **`HC-4` Zero Telemetry & Zero Unapproved Network** | Zero telemetry, analytics, crash uploaders, or auto-updaters. All `System.Net.*` socket/HTTP APIs are banned at compile time in Stage 1. | `BannedSymbols.txt` + runtime `0 TCP / 0 UDP` verification |
| **`HC-6` Self-Overhead Budget** | Desktop app CPU `<= 1.0%`, working set `<= 250 MB`, observer-effect `Present()` median disturbance `< 0.200 ms`, `p99 < 0.500 ms`, frame-time median `< 0.100 ms`. | `tools/dev/Measure-Overhead.ps1` (10-run interleaved A-B-A-B benchmark) |
| **`HC-7` Child Process Pinning** | Only the pinned PresentMon console executable may be launched, verified by SHA-256 on an open `FileStream` handle before start, with an argv allowlist that refuses `--restart_as_admin`. | `FileHashVerifier`, `PresentMonArguments`, `LauncherLintTests` |
| **`HC-8` No Dynamic Code Loading** | No `Assembly.Load*`, `Activator.CreateInstance`, `Type.GetType`, dynamic P/Invoke (`LoadLibrary`, `GetProcAddress`), or MSBuild `<Exec>` tasks. | `BannedSymbols.txt` + `RepoLintTests` |
| **`HC-9` Provenance Honesty** | Every data point carries `DataProvenance` (`Measured`, `Inferred`, `Simulated`, `Fixture`). Simulated or fixture data can never be exported as a real measurement. | `ProvenanceRules` + `IncidentBundleBuilder` |
| **`HC-10` Dependency Pinning** | Central package management (`Directory.Packages.props`) must match [`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md) exactly. | `RepoLintTests.Package_references_are_centrally_versioned...` |
| **`HC-12` Catalog-Driven Knowledge** | Vendor strings, anti-cheat service names, and protected game titles belong in `catalogs/*.json`, never hardcoded in C# product code. | `RepoLintTests.Product_code_names_no_vendor_outside_catalogs...` |

---

## 2. Building and Running the Verification Suite

### Prerequisites
- **Windows 10 (22H2+) or Windows 11 (x64)**
- **.NET 10 SDK (`10.0.402` pinned in [`global.json`](global.json))**
- Optional (for live real-PresentMon hardware tests): run `.\tools\fetch-third-party.ps1` to fetch and verify the pinned PresentMon 2.6.0 console executable against [`third_party/PresentMon/presentmon.lock.json`](third_party/PresentMon/presentmon.lock.json).

### One-Command Build, Proof & Test Pipeline
Run from PowerShell at the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` executes 6 verification stages in order:
1. **`dotnet restore` & `dotnet build -c Release`** (`TreatWarningsAsErrors=true` across all 12 projects; `0` warnings allowed).
2. **Banned-Symbol Coverage Proof (`tools/dev/verify-banned-symbols.ps1`)**: Compiles [`tools/dev/analyzer-coverage/Coverage.cs.txt`](tools/dev/analyzer-coverage/Coverage.cs.txt) and proves that every single entry in [`BannedSymbols.txt`](BannedSymbols.txt) fires `RS0030`.
3. **Analyzer Wiring Proof (`tools/dev/verify-analyzer-wiring.ps1`)**: Queries MSBuild across all 7 product projects to prove `Microsoft.CodeAnalysis.BannedApiAnalyzers` and `BannedSymbols.txt` cannot be shadowed or disabled.
4. **Full Test Suite (`2,505` tests across 5 test projects)**: Runs sequentially (`-m:1`) with a 5-minute hang guard so timing-sensitive frame-pacing and ring-buffer concurrency tests are never starved.
5. **Smoke Gate Self-Test (`tools/dev/test-smoke-gate.ps1`)**: Verifies that [`tools/dev/Assert-ProbeMatrix.ps1`](tools/dev/Assert-ProbeMatrix.ps1) accepts a healthy capability matrix and rejects 7 mutated matrices.
6. **Live CLI Smoke (`jankscope probe --json`)**: Executes the compiled CLI against the local machine and validates all 41 capability rows and 14 probes.

---

## 3. Rules for Working with Native APIs, WPF, and Encoding

1. **Native Imports (`[LibraryImport]`):**
   - Only `Jankscope.Platform` may contain `unsafe` code or `[LibraryImport]` declarations.
   - Every `[LibraryImport("dll", EntryPoint = "Name")]` must use literal strings, must be restricted to `System32` via `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`, and must be registered in `ApprovedLibraryImports` in [`tests/Jankscope.Core.Tests/RepoLintTests.cs`](tests/Jankscope.Core.Tests/RepoLintTests.cs).
2. **WPF & XAML Reflection Avoidance:**
   - Do not attach event handlers inside a XAML `DataTemplate` or reference local assembly types directly in XAML where WPF would emit `GeneratedInternalTypeHelper.g.cs` (which calls `Activator.CreateInstance` and triggers `RS0030`). Wire events and custom controls (`TimelineView`) in C# code-behind instead.
3. **ASCII & UTF-8 Hygiene:**
   - All `.cs` and `.ps1` files must remain strictly ASCII (`< 0x80`) so invisible Unicode format characters can never hide an identifier from `CSharpScanner` and Windows PowerShell 5.1 never misreads a BOM-less script.
   - All text files (`.md`, `.json`, `.yml`, etc.) are checked by `Text_files_are_valid_utf8_with_no_mojibake`.
4. **Safety-Critical Tests Must Be Shown Able to Fail:**
   - Never weaken a test to make a build pass. When adding a safety check, guard, or redactor pattern, verify via a mutation check that breaking the rule turns the test red.

---

## 4. Submitting Changes

1. Run `.\build.ps1` locally and ensure `0` warnings and `0` test failures.
2. If you changed UI layouts in `Jankscope.App`, regenerate and inspect the visual snapshots:
   ```powershell
   $env:JANKSCOPE_SNAPSHOT_DIR = "$PWD\docs\assets\screenshots"
   dotnet test tests\Jankscope.App.Tests\Jankscope.App.Tests.csproj -c Release
   ```
3. Update [`docs/STATUS.md`](docs/STATUS.md) and [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md) if your change resolves a limitation or completes a milestone item.
