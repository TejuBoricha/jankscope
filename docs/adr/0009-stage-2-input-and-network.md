# ADR 0009 - Stage 2 Architecture: Input and Network Diagnostic Labs

**Status:** Proposed (Stage 2 Planning Milestone)  
**Spec Reference:** Section 8, Section 9, HC-1, HC-4, HC-5, HC-13  
**Deciders:** Core Engineering Team  

---

## 1. Context

Stage 2 completes the Minimum Viable Product (MVP) tier of Jankscope. While Stages 1a and 1b established frame-level presentation timing and stutter capture via Intel PresentMon, many perceived "stutters" in gaming are not rendering delays at all:
1. **Input Stalls:** Games rendering at 144+ FPS can drop or queue user input due to high polling rate processing bottlenecks (e.g. 8000 Hz mouse polling flooding the main thread), USB bus resets, or thread contention.
2. **Network Jitter & Packet Loss:** Online multiplayer games suffer micro-teleportation or hit-registration delays caused by bufferbloat, Wi-Fi channel congestion, or DNS latency spikes that players misdiagnose as graphics stutters.

Jankscope must separate rendering hitches from input and network anomalies with empirical precision while maintaining strict safety constraints (zero keylogging per HC-4, zero telemetry per HC-5).

---

## 2. Decisions

### 1. Raw Input Timing-Only Collector (HC-4)
- **Mechanism:** The application registers for Windows Raw Input notifications (`WM_INPUT`) scoped exclusively to mouse movement (`UsagePage = 1, Usage = 2`).
- **Timing Focus:** The collector measures only the **inter-arrival interval** between messages ($\Delta t$) and packet delta magnitude ($\Delta x, \Delta y$).
- **Anti-Keylogger Invariant (HC-4):** Keyboard raw input is **never** registered or collected. Keystrokes cannot be reconstructed. All keyboard state APIs (`GetAsyncKeyState`, `GetKeyboardState`) remain strictly banned by static analyzers.

### 2. Kernel-PnP USB Event Tracker
- **Mechanism:** Subscribe to Windows Event Log provider `Microsoft-Windows-Kernel-PnP` (`System` log) for device lifecycle events:
  - Event 400: Device configure attempted.
  - Event 410: Device configure completed.
  - Event 411: Device configure failed.
  - Event 420: Device deleted / surprise removal.
  - Event 430: Device reset / sleep transition.
- **Correlator:** Correlates USB device re-enumeration events with sudden input silence.

### 3. Motion-Continuity Differential Diagnosis (Two-Class Model)
- **Problem:** When a user lifts the mouse or stops moving, raw input packets cease. This must not be misidentified as an input stall.
- **Solution:** A two-class control validity detector:
  - **Motion State:** Moving with steady velocity $\to$ sudden gap with subsequent jump $\to$ **Input Stall**.
  - **Deceleration State:** Normal deceleration to zero delta $\to$ **User Idle**.

### 4. Local-Only Network Latency & WLAN Probes (HC-5)
- **Gateway Probe:** Periodic ICMP echo requests to the local default gateway (e.g. `192.168.1.1`).
- **DNS Probe:** Benchmark DNS query resolution latency against configured system DNS server.
- **WAN Anchor:** Optional latency ping to root DNS resolvers (e.g. `1.1.1.1`, `8.8.8.8`).
- **WLAN Posture:** Query `WlanGetNetworkBssList` for RSSI signal strength, link speed, and channel band (2.4 GHz vs 5 GHz vs 6 GHz). Gracefully degrades to "unavailable" if Windows Location permission is denied.

---

## 3. Work Package Decomposition

| Package | Title | Primary Deliverable |
|---|---|---|
| **WP-2A** | Raw Input Timing Collector | `Jankscope.Platform/Input/RawInputCollector.cs` (timing only) |
| **WP-2B** | Kernel-PnP USB Monitor | `Jankscope.Platform/Input/UsbEventMonitor.cs` (Event 400–430) |
| **WP-2C** | Network & Gateway Latency Probes | `Jankscope.Platform/Network/GatewayPingProbe.cs` |
| **WP-2D** | WLAN Quality & Posture Probe | `Jankscope.Platform/Network/WlanPostureProbe.cs` |
| **WP-2E** | Differential Diagnosis Correlator | `Jankscope.Core/Diagnosis/InputNetworkCorrelator.cs` |
| **WP-2F** | Desktop UI Input & Network Views | `Jankscope.App/Views/InputNetworkView.xaml` |

---

## 4. Consequences & Mitigations

- **Antivirus False Positives (R2):** Raw Input registration can trigger heuristic flags in overly aggressive security software. Mitigation: Scoped purely to HID mouse class; zero keyboard raw input; open source code verification.
- **Windows 11 Wi-Fi Privacy Restrictions:** Querying Wi-Fi BSSID on Windows 11 22H2+ requires Location permission. Mitigation: Degrade gracefully to signal-strength only with clear explanatory text if location permission is disabled.
