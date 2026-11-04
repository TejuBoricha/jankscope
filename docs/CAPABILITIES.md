# Runtime Capability Matrix & Coverage

Jankscope reports what a system can and cannot measure honestly. Every runtime capability row states its identifier, tier, availability, data source, and exact reason when unavailable — never a fabricated default (spec §6.4).

---

## 1. Capability Matrix Rows (41 Items)

| ID | Title | Tier | Source | Provenance | Description / Notes |
|---|---|---|---|---|---|
| `os.version` | Windows version & build | A | Registry / Kernel | Measured | OS release, build, and revision number |
| `os.edition` | Windows edition | A | Registry / WMI | Measured | Windows SKU (Home, Pro, Enterprise, etc.) |
| `os.support` | Windows support state | A | OS Probe | Inferred | Validates supported build (Windows 11 22H2+ or 10 22H2) |
| `os.smart_app_control` | Smart App Control state | A | Registry | Measured | Evaluates SAC enforcement state |
| `privilege.elevation` | Process elevation | A | Process Token | Measured | Whether Jankscope is elevated or standard user |
| `privilege.integrity` | Mandatory integrity level | A | Process Token | Measured | Token integrity (Medium, High, System) |
| `privilege.performance_users` | Performance Log Users membership | A | Token Groups | Measured | Grants unelevated real-time ETW access |
| `etw.realtime_session` | Real-time ETW access | A | Advapi32 | Measured | Probes ability to start/query unprivileged trace session |
| `etw.session_slots` | Free ETW session slots | A | QueryAllTraces | Measured | Tracks available logger slots against the 64-session ceiling |
| `security.secure_boot` | Secure Boot status | A | Firmware / WMI | Measured | Hardware boot integrity verification |
| `security.hvci` | Memory integrity (HVCI) | A | System Probes | Measured | Hypervisor-protected Code Integrity running state |
| `security.virtualization` | Hardware virtualization (VBS) | A | WMI / CPUID | Measured | SLAT / VT-x / AMD-V hardware support |
| `hardware.cpu.model` | Processor model | A | Registry / WMI | Measured | CPU marketing name and generation |
| `hardware.cpu.topology` | Core & thread topology | A | LogicalProcessorInfo | Measured | Physical cores, logical threads, NUMA nodes, cache |
| `hardware.memory.total` | Installed physical RAM | A | GlobalMemoryStatus | Measured | Total usable system physical memory in GiB |
| `hardware.chassis` | System chassis form factor | A | SMBIOS / WMI | Measured | Desktop, Laptop, Mini-PC, Handheld |
| `hardware.power_source` | Power supply source | A | SystemPowerStatus | Measured | AC power vs. DC battery operation |
| `hardware.battery_state` | Battery charge percentage | A | SystemPowerStatus | Measured | Remaining battery percentage or N/A on desktop |
| `display.primary.resolution` | Display resolution | A | DisplayConfig | Measured | Desktop pixel resolution and DPI scaling |
| `display.primary.refresh_rate` | Display refresh rate | A | DisplayConfig | Measured | Native refresh rate in Hertz (e.g. 144 Hz, 240 Hz) |
| `display.primary.vrr` | Variable Refresh Rate | A | DisplayConfig | Measured | G-Sync / FreeSync / Adaptive-Sync capability |
| `gpu.adapters` | GPU adapters list | A | DXGI Factory | Measured | Enumerates adapters by LUID without waking sleeping dGPUs |
| `managed.domain_join` | Active Directory domain join | A | NetGetJoinInfo | Measured | Detects corporate domain membership |
| `managed.entra_join` | Microsoft Entra (Azure AD) join | A | NetGetAadJoinInfo | Measured | Detects cloud enterprise identity join |
| `managed.mdm_enrollment` | MDM enrollment status | A | MdmRegistration | Measured | Probes Intune / mobile device management presence |
| `managed.s_mode` | Windows S Mode state | A | Registry | Measured | Detects restricted S-Mode execution |
| `managed.applied_policies` | Group Policy / CSP policies | A | Registry / Security | Measured | Flags presence of external administrative locks |
| `environment.virtual_machine` | Virtual machine detection | A | BIOS / CPUID | Measured | Detects Hyper-V, VMware, VirtualBox, KVM |
| `environment.cloud_provider` | Cloud hypervisor provider | A | SMBIOS / Metadata | Measured | Identifies Azure, AWS, GCP virtual instances |
| `anticheat.resident` | Resident kernel anti-cheat | A | Service Manager | Measured | Detects Vanguard (`vgk`), EAC, BattlEye services |
| `anticheat.state` | Anti-cheat safety posture | A | Catalogs / Probe | Inferred | Governs collector suppression to prevent false flags |
| `game.session` | Active foreground game | A | Catalogs / PresentMon | Measured | Matches active window to known game catalog |
| `pdh.cpu_usage` | CPU total utilization counter | A | PDH Subsystem | Measured | Sampling state for processor time performance counters |
| `pdh.gpu_engine` | GPU 3D engine utilization | B | PDH Subsystem | Measured | Hardware vendor GPU engine utilization counters |
| `pdh.dpc_isr_rate` | DPC/ISR execution rate | B | PDH Subsystem | Measured | System interrupt service and DPC queue rates |
| `pdh.disk_queue` | Storage disk queue length | A | PDH Subsystem | Measured | Storage response and pending transfer queues |
| `frame.capture` | Frame-level presentation capture | C | Derived / PresentMon | Measured | Real-time presentation delta measurement |
| `mode.optimize` | Optimize mode eligibility gate | A | Derived (HC-11) | Inferred | Unlocks recommendations only on unmanaged systems |
| `input.devices` | Raw Input & peripheral timing | A | Planned (Stage 2) | None | Pointing device timing and polling frequency |
| `network.probes` | Network ping & gateway latency | A | Planned (Stage 2) | None | Gateway, DNS, and WAN round-trip latency |
| `platform.probes.health` | Collector probe health ledger | A | Matrix Builder | Measured | Aggregate success/failure count across all 14 probes |

---

## 2. Frame-Capture Coverage Matrix (Spec §6.6)

The table below defines how Jankscope captures frame timing across diverse system environments, privilege states, and anti-cheat conditions:

| Environment | User Token | Game Session Active? | Anti-Cheat Detected? | Frame Capture Strategy | User Action Required |
|---|---|---|---|---|---|
| Windows 11 Desktop/Laptop | Standard User (in `Performance Log Users`) | Yes | No | Direct unprivileged ETW capture via PresentMon | None (group already configured) |
| Windows 11 Desktop/Laptop | Standard User (not in group) | Yes | Any | Fallback: prompt user to join group or run elevated helper | Add user to `Performance Log Users` group |
| Windows 11 Desktop/Laptop | Elevated Admin | Yes | No | Direct ETW capture via PresentMon | None |
| Protected Title Active | Standard User | Yes | Vanguard / EAC (`vgk`) | ETW presentation metrics allowed; all inspection suppressed | None (passive user-mode ETW is undetectable) |
| Windows 10 22H2 | Standard User (in group) | Yes | No | Direct ETW capture supported | None |
| Virtual Machine (Hyper-V/KVM) | Standard User | Yes | No | Capture allowed; metrics flagged as VM-virtualized | Informational notice |
| Domain-Joined / MDM Managed | Standard User | Yes | Any | Capture allowed; Optimize mode locked to Recommend-only | Cannot apply automated system tweaks (HC-11) |
