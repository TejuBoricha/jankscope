# Frequently Asked Questions (FAQ)

---

### What is Jankscope?
Jankscope is a precision diagnostic tool for Windows gaming PCs that identifies the root cause of intermittent frame rate drops (stutters), input stalls, and presentation hitches. It is built to replace anecdotal "tweaking" advice with empirical, measurable evidence.

### Will Jankscope get me banned by anti-cheat software?
Jankscope is designed strictly to avoid anti-cheat triggers:
- **No injection:** It never loads DLLs into game processes.
- **No memory reads:** It never reads or modifies game virtual memory (`ReadProcessMemory` / `WriteProcessMemory`).
- **No hooks:** It never installs Windows API hooks (`SetWindowsHookEx`).
- **No overlays:** It draws nothing over the game window.
- **No kernel drivers:** It runs entirely in user mode with no third-party drivers.

Frame pacing data is captured externally through Windows Event Tracing for Windows (ETW) via Intel PresentMon. While this adheres to standard operating system diagnostics, individual anti-cheat systems operate with closed rules. If in doubt, do not capture during ranked competitive play.

### Why doesn't Jankscope have "One-Click FPS Booster" buttons?
Most internet "optimization guides" apply destructive, untested changes (disabling Windows Defender, deleting random services, disabling page files, or applying blanket registry hacks) that harm system stability and security with zero measurable performance gain.

Jankscope follows a strict rule: **measure first, diagnose second, recommend third**. System modifications are evaluated only in Stage 4, are completely optional, require explicit user approval with documented preconditions, and must have verified automated rollback.

### Why do I see a prompt about "Performance Log Users"?
Windows protects access to real-time event tracing sessions. To capture frame presentation timing as a standard, non-elevated user, your Windows user account must belong to the built-in **Performance Log Users** group. Adding your account to this group is a one-time administrative setup that allows unelevated diagnostic tracing without requiring you to run Jankscope as an Administrator.

### What is the difference between a "Stutter" and an "Input Stall"?
- **Stutter:** A presentation delay where the game engine fails to present a new video frame at the expected refresh interval (e.g., taking 60 ms instead of 16.6 ms), causing visible motion hitching on display.
- **Input Stall:** A condition where the game engine is drawing frames smoothly (high FPS), but input messages from your mouse or keyboard are dropped, delayed, or queued, leading to unresponsive controls or missed actions.

### What is a "1% Low" and "Frame Pacing"?
- **Frame Pacing:** The consistency of intervals between displayed frames. A game running at a constant 60 FPS should deliver one frame every 16.67 ms. If frames arrive unevenly (e.g., 8 ms, then 33 ms), motion appears jerky even if the average FPS reads 60.
- **1% Low:** The frame rate calculated from the 99th percentile slowest frame times. It reflects the severity of worst-case drops rather than smooth averages.

### Why does my mouse report "polling rate not exposed"?
Windows Raw Input reports mouse movement deltas when the cursor moves, but standard USB HID drivers do not expose configured device polling frequency (e.g. 1000 Hz vs 8000 Hz) to unprivileged user-mode applications without vendor-proprietary software. Jankscope estimates effective polling rate dynamically during sustained motion.

### Does Jankscope work offline?
Yes. Jankscope has zero network dependencies. All detection, data analysis, timeline rendering, and report generation take place entirely on your local machine.
