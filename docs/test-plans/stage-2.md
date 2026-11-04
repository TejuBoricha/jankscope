# Test Plan - Stage 2 (Input and Network Labs)

Written **before** implementation (spec section 1 #3).  
**Objective:** Complete the Minimum Viable Product (MVP) tier by adding Raw Input timing, USB hardware event tracking, motion-continuity differential diagnosis, and local network latency diagnostics.

---

## 1. Safety & Architecture Invariants

- **HC-4 (No Keylogging):** Scoped exclusively to mouse raw input (`UsagePage = 1, Usage = 2`). Keystrokes cannot be collected or reconstructed. Keyboard state APIs (`GetAsyncKeyState`, `GetKeyState`, `GetKeyboardState`) remain banned by Roslyn analyzer and regex lints.
- **HC-5 (Local-Only Egress):** Zero user data sent outbound. Network probes send minimal ICMP echo requests to local default gateway or standard DNS IP addresses (e.g. `1.1.1.1`).
- **HC-9 (Honest Provenance):** Inferred input polling frequencies and calculated packet deltas are tagged `Inferred`. Simulated test packets are tagged `Simulated`.

---

## 2. Test Matrix

| # | Subsystem | Level | Invariant / Requirement to Prove | Test Scenarios |
|---|---|---|---|---|
| **1** | Raw Input Timing | Unit + Mock | Measures inter-message interval ($\Delta t$) and motion magnitude without storing keystrokes or text | Synthetic 1000 Hz, 4000 Hz, 8000 Hz packet streams; burst packet arrival; zero keyboard raw input allowed in registration |
| **2** | Motion Continuity | Unit + Property | Distinguishes deliberate mouse stop/lift (deceleration) from sudden input stall (abrupt drop at high velocity) | Steady moving cursor with 50 ms gap $\to$ flag Input Stall; smooth deceleration to 0 $\to$ flag User Idle; noise filtering |
| **3** | Kernel-PnP USB Tracker | Unit (Log Seam) | Maps Windows Event Log Kernel-PnP events (400, 410, 411, 420, 430) to peripheral disconnect/reset incidents | Injected Event 420 (surprise removal) correlates with input silence; Event 411 (config failed) flags USB driver stall |
| **4** | Gateway Latency Probe | Unit + Seam | Measures ICMP ping to default gateway without administrative privileges | Unreachable gateway reports `Unavailable: Gateway timeout`; high jitter (> 20 ms) flags local Wi-Fi / powerline hop issue |
| **5** | DNS Resolution Probe | Unit + Mock | Measures query round-trip time; distinguishes DNS server failure from general packet loss | Fast resolution (< 10 ms); timed-out query (2000 ms); fallback when offline |
| **6** | WLAN Posture Probe | Unit + Platform | Queries Wi-Fi signal strength, channel frequency, and BSSID without violating Windows 11 location privacy | Location permission granted $\to$ reports RSSI and band (5 GHz vs 2.4 GHz); Location disabled $\to$ reports `Unavailable: Location permission disabled` without crashing |
| **7** | Differential Diagnosis | Integration / Golden | Cross-correlates frame pacing hitch vs. input polling stall vs. network packet loss in single incident report | Frame hitch only (FPS drops, input steady); Input stall only (FPS constant, input drops); Network drop only (FPS and input steady) |
| **8** | Static & Lint Safety | Static Analysis | Proof that no keyboard hooks, no unapproved native calls, and no ad SDKs are introduced | `RepoLintTests` passes; zero RS0030 analyzer warnings |

---

## 3. Order of Work (Test-Driven Development)

1. **WP-2A:** Raw Input timing structures, ring buffer for input delta intervals, and motion continuity detector in `Jankscope.Core` and `Jankscope.Platform`.
2. **WP-2B:** Kernel-PnP event reader and USB stall detector in `Jankscope.Platform`.
3. **WP-2C:** Gateway ping and DNS latency probe implementations in `Jankscope.Platform`.
4. **WP-2D:** WLAN posture reader with Windows 11 location privacy fallbacks.
5. **WP-2E:** Multi-factor incident correlator integrating frame pacing with input and network timelines in `Jankscope.Core.Reports`.
6. **WP-2F:** Desktop UI visualization for input polling intervals and network latency graphs.
7. **Verification:** Live end-to-end test against synthetic harness injecting both frame hitches and input stalls.
