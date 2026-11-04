# Support & Feedback Guide

Jankscope is an open-source, community-driven project maintained under the MIT license. Support is provided on a best-effort basis; no commercial SLA or guaranteed turnaround time is promised.

---

## 1. Where to Get Help

- **Community Discussions & Questions:** [GitHub Discussions](https://github.com/TejuBoricha/jankscope/discussions)
- **Bug Reports & Issues:** [GitHub Issues](https://github.com/TejuBoricha/jankscope/issues)
- **Security Vulnerabilities:** See [SECURITY.md](../SECURITY.md) for confidential reporting instructions.

---

## 2. Generating a Redacted Support Bundle

When reporting an issue with hardware detection, stutter diagnosis, or capture failure, please attach a redacted **Compatibility Report**:

1. Open Jankscope.
2. Navigate to **About and privacy**.
3. Under the compatibility section, click **Export compatibility report (you see a preview first)...**
4. Review the preview text. Verify that your personal username, machine name, and IP addresses have been replaced with placeholders like `<USER>`, `<HOST>`, and `<IP>`.
5. Click **Save as...** and save the `.json` file to your computer.
6. Attach this file to your GitHub Issue.

If your issue involves a specific stutter capture, export the incident from the **Stutter investigator** page using the **Export...** button with redaction enabled.

---

## 3. Reporting a Wrong Diagnosis

A key goal of Jankscope is diagnostic accuracy. If Jankscope attributed a frame drop to the wrong subsystem (e.g., claiming a CPU pacing hitch when GPU VRAM was exhausted, or missing an input polling stall):

1. Open a new issue using the **Wrong Diagnosis** template.
2. Include:
   - Your system specs (or attach the redacted Compatibility Report).
   - The game name and rendering API (DirectX 11, DirectX 12, Vulkan).
   - The diagnosis Jankscope provided vs. the true underlying cause confirmed by your testing.
   - The attached redacted incident report `.json`.
3. Validated wrong-diagnosis reports are converted directly into automated regression fixtures to permanently improve the diagnostic rules.

---

## 4. Anti-Cheat Statement

Jankscope uses only documented, unprivileged Windows Event Tracing for Windows (ETW) via Intel PresentMon. It does not open handles to game processes, inject dynamic link libraries (DLLs), install kernel filter drivers, or render in-game overlays.

However, anti-cheat providers (such as Vanguard, Easy Anti-Cheat, BattlEye, and Ricochet) do not publish white-lists of permitted diagnostic software. If you are uncertain about whether a specific competitive title permits external tracing tools, do not run Jankscope while playing that title.
