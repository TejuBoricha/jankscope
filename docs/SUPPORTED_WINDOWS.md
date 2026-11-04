# Supported Windows Versions

Jankscope targets modern Windows personal computers used for gaming. This document defines the supported operating system tiers, architectural requirements, and degradation behaviors.

---

## 1. Operating System Support Matrix

| OS Version | Build Range | Architecture | Support Status | Notes |
|---|---|---|---|---|
| **Windows 11 24H2** | Build 26100+ | x64 | **Tier 1 (Fully Supported)** | Primary development and test platform. |
| **Windows 11 23H2** | Build 22631 | x64 | **Tier 1 (Fully Supported)** | Full diagnostic and capture capability. |
| **Windows 11 22H2** | Build 22621 | x64 | **Tier 1 (Fully Supported)** | Full diagnostic and capture capability. |
| **Windows 10 22H2** | Build 19045 | x64 | **Tier 2 (Best-Effort)** | Supported until Windows 10 reaches end of life. |
| **Windows 11 ARM64** | Build 22621+ | ARM64 | **Planned (P2)** | Requires ARM64 native builds of PresentMon and bindings. |
| **Windows 10 (Pre-22H2)** | Builds < 19045 | x64 | **Unsupported** | Missing required DXGI and ETW event manifests. |
| **Windows 7 / 8 / 8.1** | Any | Any | **Unsupported** | Lacks .NET 10 runtime support. |
| **Windows Server** | Any | Any | **Unsupported** | Server SKUs lack desktop composition and consumer GPU drivers. |
| **Windows in S-Mode** | Any | Any | **Unsupported** | Policy blocks execution of bundled executables. |

---

## 2. Platform Requirements

- **Processor:** x64 64-bit processor (Intel 8th Gen+ or AMD Zen 2+ recommended).
- **Memory:** Minimum 4 GiB physical RAM (8 GiB+ recommended).
- **Display Adapter:** DirectX 11.0 feature level capable GPU or higher with WDDM 2.0+ drivers.
- **Privilege Requirements:** Runs as a non-elevated Standard User. Real-time frame capture requires membership in the `Performance Log Users` local group or administrative elevation.

---

## 3. Degradation & Graceful Failure

When started on an unsupported or partially supported operating system:
1. `os.support` capability row is marked `Unavailable: unsupported Windows`.
2. `mode.optimize` gate locks the application strictly to **Recommend-only** mode (HC-11).
3. The application displays an explanatory banner indicating which features are disabled, rather than crashing or providing misleading performance data.
