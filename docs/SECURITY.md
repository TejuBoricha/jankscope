# Security Architecture & Threat Model

> [!NOTE]
> For reporting security vulnerabilities and our coordinated disclosure policy, please see the repository root [SECURITY.md](../SECURITY.md).

---

## 1. Trust Boundaries and System Architecture

Jankscope adheres to strict least-privilege security boundaries:

```mermaid
flowchart TD
    User["Standard User Session"] --> UI["Jankscope GUI (Unelevated WPF)"]
    UI --> Engine["In-Process Engine (Unelevated)"]
    Engine --> PM["PresentMon Process (Pinned CLI, Hash-Verified)"]
    Engine --> Probes["Read-Only System Probes (PDH, DXGI, ETW)"]
    
    subgraph Trust Boundary [Elevation & System Integrity Boundary]
        Helper["Elevated Helper Service (Stage 3+)"]
        Kernel["Windows Kernel & Hardware Drivers"]
    end
    
    Engine -.->|Named Pipe (Authenticated Token)| Helper
    Helper --> Kernel
```

---

## 2. Threat Analysis & Mitigations

### T-1: Anti-Cheat Triggering & False Bans (HC-1, HC-2)
- **Threat:** Anti-cheat software (Vanguard, Easy Anti-Cheat, BattlEye, Ricochet) flags Jankscope as a cheat, leading to account penalties or game crashes.
- **Mitigation:**
  - Zero code injection (`CreateRemoteThread`, `WriteProcessMemory` banned by static analysis).
  - Zero API hooking (`SetWindowsHookEx`, `SetWinEventHook` banned).
  - Zero process handles opened to game executables (names collected only via unprivileged ETW events and performance counters).
  - Zero in-game overlays (no DirectX swap-chain hooking).
  - Zero third-party kernel drivers loaded (HC-2).

### T-2: Command Injection & Subprocess Abuse (HC-7, ADR 0003)
- **Threat:** Manipulating arguments passed to child executables (specifically `PresentMon-2.6.0-x64.exe`) to execute arbitrary code or trigger unauthorized administrative escalation.
- **Mitigation:**
  - `PresentMonProcessLauncher` validates executable SHA-256 against a cryptographic lockfile (`presentmon.lock.json`) before every execution.
  - Arguments are constructed using structured `ProcessStartInfo.ArgumentList` (never joined shell strings).
  - Strict allowlist grammar: options like `--restart_as_admin` or unvetted flags throw immediate argument exceptions.

### T-3: Keystroke Logging & Credential Interception (HC-4)
- **Threat:** User mark hotkeys intercept passwords or sensitive inputs.
- **Mitigation:**
  - Global hotkeys use strictly `RegisterHotKey` for explicit chords registered with Windows.
  - APIs that read global keyboard state (`GetAsyncKeyState`, `GetKeyState`, `GetKeyboardState`) are banned by both Roslyn analyzers (`BannedSymbols.txt`) and regex-based repo lints.

### T-4: Inter-Process Communication (IPC) Spoofing (Stage 3+)
- **Threat:** An unprivileged malicious actor impersonates the GUI or sends unauthorized commands to the elevated helper service.
- **Mitigation:**
  - Mutual authentication over Windows Named Pipes using security descriptors restricting access to the originating user token.
  - Verification of caller process ID, executable image path, and creation timestamp.
  - Single-use cryptographically random session tokens for elevated operations.

### T-5: Deserialization & Report Injection
- **Threat:** Loading a crafted incident report or catalog JSON causes arbitrary code execution or crash loops.
- **Mitigation:**
  - `System.Text.Json` with explicit strongly-typed DTOs and `MaxDepth = 256`.
  - Polymorphic type discriminators (`$type`) and insecure binary formatters are strictly banned.
  - Schemas for all external catalogs (`catalogs/*.json`) validated at load time against strict JSON schemas.

### T-6: Supply Chain & Dependency Tampering (HC-10)
- **Threat:** Malicious package update compromises the application binary or build pipeline.
- **Mitigation:**
  - Central Package Management (`Directory.Packages.props`) with pinned versions.
  - Automated dependency vulnerability audit (`dotnet nuget audit`) in CI.
  - Automated license check and CycloneDX SBOM generation on release.
