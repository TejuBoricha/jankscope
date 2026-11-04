# External Beta Test Plan

This document establishes the recruitment criteria, hardware test matrix, test protocols, and feedback mechanisms for the external beta testing phase of Jankscope (spec §20).

---

## 1. Objectives & Scope

The external beta program validates Jankscope across real-world gaming configurations that cannot be fully replicated on a single developer bench:
- Verify unprivileged ETW capture across various Windows 11 editions and anti-cheat drivers.
- Confirm zero false flags, zero crashes, and zero performance regression while gaming.
- Validate that the automated redaction engine and export preview protect user privacy on diverse machine naming schemes.

---

## 2. Beta Cohort Hardware Matrix

We aim to recruit 15–20 volunteer beta testers representing diverse hardware configurations:

| Profile | Target Count | Hardware Target | OS Target |
|---|---|---|---|
| **High-End Desktop (NVIDIA)** | 4 | Intel Core i7/i9 or AMD Ryzen 7/9 + RTX 4070/4080/4090 | Windows 11 24H2 |
| **All-AMD Desktop** | 3 | AMD Ryzen 5/7 + Radeon RX 7800/7900 XT | Windows 11 23H2 |
| **Hybrid Gaming Laptop** | 4 | AMD/Intel CPU + NVIDIA Optimus (MUX switch / Advanced Optimus) | Windows 11 23H2 |
| **Entry / iGPU Only** | 3 | Intel Iris Xe / AMD Radeon 780M / Steam Deck or ROG Ally | Windows 11 22H2/24H2 |
| **Legacy Windows 10** | 3 | Any x64 gaming hardware | Windows 10 22H2 (Build 19045) |
| **Enterprise / Managed PC** | 2 | Domain-joined or Intune MDM enrolled gaming laptop | Windows 11 Enterprise |

---

## 3. Testing Protocol for Beta Testers

1. **Installation & First Launch:**
   - Verify unprivileged startup (no UAC prompt required).
   - Check the **About and privacy** page to verify hardware detection (CPU cores, GPU LUID, display refresh rate).
2. **Compatibility Report Export:**
   - Export a Compatibility Report and review the preview to confirm that your username, PC name, and IP are completely redacted.
3. **Live Gameplay Monitoring:**
   - Launch an uninhibited game (e.g. Aim Lab, CS2, Cyberpunk 2077).
   - Capture for 15+ minutes with `Ctrl+F11` hotkey marks upon perceived hitches.
   - Verify zero frame drops induced by Jankscope itself.
4. **Issue Reporting:**
   - Submit feedback via GitHub Issues using the beta issue template, attaching the redacted compatibility report.
