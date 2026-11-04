# Privacy Notice

> [!IMPORTANT]
> **Draft document — needs legal review.** This document provides a plain-language explanation of Jankscope's local-only architecture and privacy commitments. It does not constitute legal counsel.

---

## 1. Core Commitment: Everything Stays on Your PC

Jankscope is designed from the ground up as a strictly local diagnostic tool.
- **Zero telemetry:** Jankscope contains no analytics SDKs, crash reporters, user tracking, or telemetry pings.
- **Zero background uploads:** The application makes zero outbound internet requests during normal operation.
- **Zero advertising:** There is no advertising SDK, tracking pixel, ad network integration, or advertising identifier.
- **Offline first:** Jankscope is fully functional on air-gapped or disconnected computers.

Any file saved (such as an incident report or compatibility summary) is created only when you explicitly press Save, in the directory you select.

---

## 2. Permission and Data Collection Table

| Subsystem | What is Read | What is NOT Read | Purpose | Storage & Export |
|---|---|---|---|---|
| **Frame Timing** | Swap-chain presentation timestamps emitted by Windows ETW (`Microsoft-Windows-DxgKrnl`) via Intel PresentMon | Pixel contents, game memory, game assets, chat messages, input inside the game | Measure frame intervals, detect stutters, and isolate pacing hitches | In-memory 30 s ring buffer; discarded on exit unless marked |
| **Hotkeys** | Windows `RegisterHotKey` notifications for the configured chord (e.g., Ctrl+F11) | Keystrokes, typed text, passwords, general keyboard activity | Timestamp the exact moment you perceive a hitch (HC-4) | Not logged; timestamp used only to locate the frame window |
| **Hardware & Posture** | CPU model, GPU name, display resolution, refresh rate, OS build, driver version, memory | Files, documents, personal media, browser history, credentials | Establish hardware baseline and detect known configuration issues | Shown in capability matrix; redacted on export |
| **User Notes** | Free-form notes entered in the investigator note box | Any clipboard or unentered text | Allow users to record what was happening in-game | Stripped of user names, host names, IPs, MACs, and paths before export |

---

## 3. Redaction Guarantees (HC-16)

When exporting an incident report or compatibility summary:
1. **Automated Redactor:** Replaces all detected user profile paths (`C:\Users\<USER>`), computer names, MAC addresses, IPv4/IPv6 addresses, Wi-Fi SSIDs/BSSIDs, device serial numbers, and command-line arguments with generic placeholders (e.g., `<USER>`, `<HOST>`, `<IP>`).
2. **Full Visual Preview:** Before any report is written to disk, Jankscope displays the complete, byte-exact JSON document and reports the total count of redactions performed.
3. **Independent Export Guard:** Prior to serializing to disk, an independent validation layer (`ExportGuard`) scans the raw document and decoded strings. If any known sensitive value or pattern resembling personal data survives, **export is blocked completely** and nothing is saved.

---

## 4. Third-Party Links

The application contains optional, static links to GitHub Sponsors and the Microsoft Store on the "About and privacy" page:
- Links are never opened automatically.
- Links are never prefetched or queried in the background.
- Clicking a link opens your system's default web browser directly to that static HTTPS destination.
