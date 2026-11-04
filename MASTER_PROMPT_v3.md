# MASTER ENGINEERING PROMPT v3 — Universal Windows Gaming Performance Intelligence & Optimizer

> **Scope:** a product for **any Windows gaming PC** — desktop, laptop, handheld/mini-PC — any GPU/CPU vendor, any peripheral vendor, any display language, standard user or admin. It is **not** a tool for one machine.
> **What changed from v2:** the product is now explicitly universal. The author's PC is demoted from "target" to **reference test bench**; vendor/OS/locale/privilege assumptions moved to runtime detection and data-driven catalogs (§3); anti-cheat safety defaults ON for every game; managed or unknown configurations default to Recommend-only; user decisions recorded (§2); monetization guardrails (§4.1) and cloud compatibility testing (§18) added. See `PROMPT_REVIEW.md` for the defect list.

---

## 0. Role and mission

Act as a principal Windows desktop engineer, GPU/rendering performance specialist, USB/HID and network diagnostics engineer, security engineer and senior QA lead.

Build a **working, installable Windows desktop application** that diagnoses, explains and — only with evidence and approval — safely improves real gaming problems for **a wide population of PCs**: intermittent FPS drops, micro-stutter, wireless-mouse freezes, input interruptions and network instability. Real telemetry from the real PC: no mock dashboards, no sample data shown as live, no generic "boost" tweaks.

Optimize for frame pacing, input reliability and network stability. Never optimize for free RAM, fewer processes, lower CPU %, or number of tweaks applied. Never promise an FPS gain before measuring it. Because the agent can only test on one machine, **design for portability and be honest about what was and was not tested** (§18, §19).

---

## 1. Execution protocol (read before writing code)

1. **Ledger.** Maintain `docs/STATUS.md`: each item is `not started | in progress | done-verified | done-unverified | blocked`, with an **evidence link** (test name, command output, file). Nothing is `done-verified` because a screen, setting or stub exists.
2. **Definition of done:** code + automated test + documentation + recorded evidence + a `LIMITATIONS.md` entry for anything partial.
3. **Skills and agents — use them deliberately:**
   - architecture/ADR and system-design skills for the decisions in §6 (each ADR lists rejected alternatives);
   - a testing-strategy skill to write each stage's test plan *before* implementing it;
   - spec/acceptance skills so every requirement has a testable line;
   - a code-review pass and a simplification pass before each stage gate;
   - **separate sub-agents** for: (a) security review of the privileged helper, IPC and rollback code; (b) writing rule fixtures independently of the engineer who wrote the rule; (c) re-running every documented install/build/run command on a clean checkout to prove the docs work; (d) a portability review ("what breaks on AMD-only, non-English Windows, Windows 10, standard user, managed PC?").
   - Parallelize only across disjoint modules/files (one writer per file). Integrate and run the full suite on the main thread.
4. **Stage gates** (§17): build green, tests green, STATUS/limitations updated. **Stop after Stage 1b and Stage 4** at the validation points: write `docs/VALIDATION_<n>.md` (exact steps, expected artifacts; items tagged [U] in §19), hand it to the user, and continue **only with independent work** — do not end the run or invent the user's results. Workspaces not yet built are **hidden, not stubbed**.
5. **Questions policy.** Do not ask what inspection can answer. The user's decisions so far are recorded in §2.2; ask again only for something not covered there, with a stated default.
6. **Honesty rules.** Separate *measured / inferred / assumed*. Label simulated, replayed or fixture data as such in code, UI and reports. If a sensor, permission or tool is unavailable, say exactly that — never substitute a plausible number.

---

## 2. Decisions taken and the reference machine

### 2.1 What the reference machine is for
The author's PC is the **reference development and first-validation bench**. Its facts ground fixtures and the first live test. **Never special-case it in product code.** Any vendor-, model- or OEM-specific behaviour must come from the data-driven catalogs in §3.4, not from `if` statements.

Reference facts (read-only probe, 2026-10-16; re-verify at runtime, never hardcode):
- Lenovo laptop (`82XV`), Intel i7-13620H (10 cores/16 threads, hybrid P/E), 16 GB DDR5, two NVMe SSDs, `C:` ~74 GB free; on AC; S3 sleep (not Modern Standby).
- Windows 11 **26H2, build 26300.9711**, retail branch; registry `ProductName` still says "Windows 10" (**detect Windows 11 by build ≥ 22000, never by name**); Home Single Language edition (no gpedit, Hyper-V host, Sandbox); `en-IN`, IST.
- **Hybrid graphics:** Intel UHD + NVIDIA RTX 4060 Laptop; the internal 1920×1080 @ 144 Hz display is reported on the Intel adapter.
- Power plan Balanced; **USB selective suspend already off in the active plan** (AC/DC); HAGS on; **VBS running with HVCI**; Defender on.
- **Kernel anti-cheat active** (Riot Vanguard driver `vgk` loaded at boot; EAC service present); many protected and unprotected games installed.
- Vendor software running: Lenovo Vantage stack, Razer Synapse/Chroma stack, NVIDIA display container; a `PresentMonService` process **already running** (a child of Intel Graphics Software — another product's service; never use or disturb it; its ETW session is `pm2_igs`). About 40 ETW sessions are already active (system limit 64), including a leftover session named `TraceEventSession` from a crashed tool; `vgtray` (Vanguard) is running. The reference user is already in *Performance Log Users*; Smart App Control is off; Secure Boot is on (readable unelevated from the registry).
- Network: Intel Wi-Fi 6 on 5 GHz; Ethernet present but disconnected.
- Input: **Razer Viper V3 HyperSpeed** 2.4 GHz dongle mouse (VID 1532/PID 00B8), a Bluetooth LE HID device, built-in keyboard, ELAN touchpad.
- Toolchain: **no .NET SDK/runtime, no Visual Studio, no Windows SDK**; `git`, `node`, `winget`, `wpr.exe`, `tracerpt.exe` present; shell **not elevated**.

### 2.2 User decisions (2026-10-16)
1. **Reference freezing device:** the Razer dongle mouse. It is the first live test subject — but the lab must work for any wired/2.4 GHz/Bluetooth mouse (§8).
2. **Games:** *every* game. Support all rendering APIs and presentation modes; treat anti-cheat-protected titles as the common case (HC-1).
3. **Toolchain installs (delegated to best practice):** the agent is pre-approved to install **only** the current **.NET LTS SDK (.NET 10; .NET 8 and 9 reach end of support on 2026-11-10 and must not be targeted) from the official Microsoft package via `winget`**, pinned with `global.json`, logging package id, version, source and hash. Print the exact command before running it. Anything else (Windows ADK/WPA, Windows SDK, signing tools, any third-party binary, drivers) requires a fresh yes with source, version and size. The winget package is probably machine-scope, so **expect a UAC prompt the user must accept**; the per-user alternative is Microsoft's official `dotnet-install` script (print its URL and hash first). If the install is declined, **stop and report** — do not fall back to unvetted toolchains. The synthetic test harness is **C#** (a license-checked managed Direct3D binding such as Vortice.Windows or Silk.NET): no MSVC or Windows SDK is installed, so a C++ harness is not an option.
4. **Elevation (delegated to best practice):** least privilege. The product is fully useful **without** elevation wherever Windows allows. Anything needing admin runs in a **small, separate, signed helper started on demand through a UAC prompt for the session** — no always-on service in v1. **The app never edits group membership or other system settings to gain privileges:** if a user wants unelevated frame capture, `INSTALL.md` explains how *they* can add themselves to *Performance Log Users* and what that implies — the app only detects it. If the user lacks admin rights, elevated features are shown as unavailable with the reason.
5. **Monetization: free with ads.** Allowed only under the guardrails in §4.1 and HC-13, as a P1 feature built after the core (Stage 5), behind a feature flag, and only if the feasibility ADR finds a compliant ad network. Diagnoses and recommendations are never influenced by revenue.
6. **Compatibility testing: cloud free tiers.** The user creates and owns every cloud account; the agent **never creates accounts, enters payment details, accepts terms or spends money** and only prepares runbooks/IaC for the user to review and run (§18). Cloud VMs cover the OS/locale/privilege/installer matrix, not real GPUs or input hardware, and the compatibility document must say so.

---

## 3. Universality requirements (new — apply to every section)

### 3.1 Hardware classes the design must handle
- **Form factors:** desktop, laptop, handheld/tablet, mini-PC — detect from SMBIOS chassis type, battery and power presence, never assume. Battery-specific logic only when a battery exists; AC/DC state recorded whenever applicable.
- **GPU configurations:** NVIDIA / AMD / Intel discrete; iGPU-only; hybrid or MUX laptops; multi-GPU desktops; no usable vendor API. Vendor APIs are optional plug-ins (NVML, AMD ADLX, Intel IGCL); **PDH `GPU Engine/Memory` counters and PresentMon adapter data are the vendor-neutral baseline.**
- **CPU topology:** Intel hybrid P/E, non-hybrid, AMD with multiple CCDs or cache-asymmetric (X3D) parts, SMT on/off; use `GetLogicalProcessorInformationEx`/efficiency classes at runtime. ARM64 Windows: design managed code to run there; native dependencies are flagged; ARM64 is **not tested** in v1 and is listed as such.
- **Memory/storage:** 8–128 GB; NVMe/SATA/HDD; scale sampling and buffer sizes to the class (§14).
- **Displays:** single/multi-monitor, **mixed refresh rates**, VRR (G-SYNC/FreeSync/Adaptive-Sync), HDR, windowed/borderless/exclusive fullscreen, flip model vs composed.
- **Peripherals:** any vendor, wired/2.4 GHz/Bluetooth; high polling rates (≥ 4 kHz) as a *testable hypothesis*, never an assumption.
- **Networking:** Ethernet, Wi-Fi 5/6/6E/7, USB tether/cellular, VPN/proxy, any NIC vendor.

### 3.2 Windows and user matrix
- **OS:** Windows 11 (current releases primary), Windows 10 22H2 **best-effort via runtime feature detection** (out of mainstream support — say so in docs). Editions: Home, Pro, Enterprise, Education. Windows Server and S mode are unsupported and must be detected and explained, not crashed on.
- **Display language and region:** any. Therefore: use **English performance-counter paths** (e.g. PDH "AddEnglishCounter"); identify events by **provider + event ID, never by message text**; use the **WLAN API**, not parsed `netsh` output; store UTC/ISO-8601; format numbers/dates via culture; UI strings in resource files from day one.
- **Privilege contexts:** the default user is a **standard user**. Tier A features (below) must work with no elevation. Test both standard-user and admin parity.
- **Managed devices:** detect domain-join, MDM enrollment, applied policies, S mode, and virtual machines via documented APIs. On a managed device the app runs in **Recommend-only** (HC-11) and explains why; in a VM it warns that GPU/timing telemetry is limited.

### 3.3 Capability tiers (each feature declares one and degrades gracefully)
- **Tier A — works everywhere, unelevated where Windows permits:** PDH counters, WMI/CIM, event logs, device enumeration/notifications, WLAN API, ICMP/DNS probes, Raw Input *timing*.
- **Tier B — vendor- or hardware-conditional:** NVML, ADLX, IGCL, vendor thermal/GPU-mode readers, battery/HID vendor data.
- **Tier C — elevated or external tool:** ETW kernel DPC/ISR, USB analytic tracing, WPR captures, and — depending on the ADR in §6.6 — **frame-level capture**. Where Tier C is unavailable, the app **still delivers** CPU/GPU/memory/storage/network/input diagnostics and clearly states "frame pacing unavailable without X" instead of faking lows.

### 3.4 Data-driven knowledge catalogs (new)
Versioned, schema-validated JSON (or equivalent), data-only (never code), shipped with the app, user-extensible, each entry with source and `lastReviewed`:
- anti-cheat services/drivers (Vanguard, EAC, BattlEye, FACEIT, ESEA, Ricochet, VAC, GameGuard, XIGNCODE3, ACE, others);
- OEM utilities, peripheral/RGB suites, overlays, capture/streaming and monitoring tools, vendor "game booster" apps (to flag as *possible* interference, never as proven);
- known heavy background workloads (indexing, sync, updates, scanners);
- vendor GPU-mode and thermal-profile readers (documented interfaces only); event providers/IDs of interest with Microsoft documentation references.
Product logic never hardcodes vendors or models. A lint/test (§19 #17) enforces this.

### 3.5 Safety scales with uncertainty (new)
Unknown hardware, OS, vendor or managed state ⇒ **default deny for any change** (Recommend-only). Optimization actions declare an **eligibility allowlist by detected configuration**; if detection is partial or ambiguous, the action is not offered for apply. Catalog updates after release are signed data packs (P2); v1 has no remote kill-switch and no automatic content download.

### 3.6 Compatibility evidence without telemetry (new)
- **Self-test / Compatibility Report:** one button exports a **redacted** report (OS build, edition, CPU/GPU class and driver versions, runtime capability matrix, collector health and failures, tool versions). No personal data; user previews it; sharing is manual.
- **Fixture ingestion:** a developer tool converts a compatibility report or a recorded session (with the user's consent) into a regression fixture so unseen configurations become tests.
- `docs/COMPATIBILITY.md` is an honest **tested / untested / expected-to-work** matrix and a recommended external beta matrix (NVIDIA desktop, AMD desktop, AMD+NVIDIA laptop, Intel iGPU-only, handheld, Windows 10, non-English locale, standard-user, managed PC).

---

## 4. Product definition

**Users:** from non-technical gamers who want plain answers to enthusiasts who want raw evidence. Default to plain language with an "Advanced" layer.

**Goals (measurable):**
- G1. For a marked stutter incident, present ranked, evidence-backed hypotheses with a stated grade (§11) and what would falsify each.
- G2. For a marked input stall, decide whether it is **device/link-level or system-level**, or report inconclusive (§8).
- G3. Characterize network stability to gateway, DNS and chosen targets with documented method and honest limits.
- G4. Apply only validated, reversible changes, each with measured before/after and tested rollback.
- G5. Stay within the overhead budget (§14) on every hardware class, proven by measurement.
- G6. Remain useful on any supported configuration, degrading honestly (§3.3).

**Non-goals:** overclocking, voltage/power-limit/BIOS/firmware changes; automatic driver/firmware installs; code injection, hooks, overlays or memory reads in game processes; any kernel driver of our own; anti-cheat bypass; "boosting" game-server ping via DNS/TCP/MTU tweaks; registry/RAM cleaners; any cloud backend or telemetry business (third-party ad delivery under §4.1 is the only network service); Windows below the §3.2 matrix; a "game booster" marketplace.

### 4.1 Monetization — free with ads (user decision, 2026-10-16)

**Feasibility comes first (Stage 0 ADR, research only — write no ad code until it is approved).** Verify current terms before choosing anything:
- Google AdSense policy **prohibits embedding its ads in software applications, including desktop apps**, so it is not an option; AdMob is mobile-only. Microsoft's old UWP ad platform was shut down in 2020.
- Candidates to *verify, not endorse*: the Liftoff/Vungle Windows desktop SDK (WebView2-based; oriented to game/mobile video formats), Microsoft's in-app ads SDK (its launch status was unconfirmed in documentation), and smaller networks with stale public material. **Reject** pay-per-install "offer" networks (they violate the no-bundled-offers rule and the product's trust model) and anything that behaves like a toolbar, extension or injector.
- Check each network's current ToS for desktop placement, supported formats (**banner/native only — no video, audio, interstitials or pop-ups**), consent handling, privacy behaviour, minimum traffic, payout thresholds and viewability reporting. Do not promise revenue: a niche diagnostic tool may earn little, so record assumptions and let the user decide.
- If no compliant network exists, **stop and report**, and offer alternatives for the user to choose from (clearly labeled house or sponsor slots, a donation link, a later paid ad-free tier).

**If approved (HC-13 guardrails, all testable):**
- **Where:** one fixed, small slot on Overview and Reports/Settings only. Never on Live Monitor, Stutter Investigator, Mouse & USB Lab, Network Lab, Drivers & System Health, Optimization Center or Benchmarks.
- **When:** only while the UI is visible and no game session is detected; the ad host is **disposed** when the UI is hidden, minimized or a game is running; zero ad activity when no UI exists. First-run disclosure and consent before the first ad request.
- **Isolation:** WebView2 with its own user-data folder, a navigation allowlist and CSP, **no host objects or local-API access**, no downloads, no redirects, no file access. Never loaded in the Engine or helper.
- **Privacy:** ad requests carry no diagnostic, hardware, process, network or incident data and no advertising ID beyond what the protocol inherently sends (IP, user agent). **Contextual, non-personalized ads by default worldwide**; personalization only through a lawful consent flow the network requires, never for minors (rules such as India's DPDP Act, COPPA and the UK Children's Code restrict ads aimed at minors, and gaming audiences include minors — this needs legal review, it is not legal advice).
- **Integrity:** block ad categories that conflict with the product or are commonly scams — PC optimizers/cleaners/boosters, driver updaters, registry tools, "system scan" scareware, VPN/antivirus upsells, gambling, adult, crypto. Revenue never influences diagnoses, grades, recommendation order or wording; no affiliate links inside findings; any sponsored content is labeled.
- **Resilience:** offline, blocked (user ad-blocker) or failed ads leave an empty or house placeholder with no error and no feature loss.
- **Measured cost:** ad activity is inside the §14 budgets whenever the UI is open, and is measured.
- **User-owned tasks (the agent cannot do these):** ad-network account and approval, tax/payout details, publishing the privacy policy, store-policy compliance.

---

## 5. Hard constraints (violating any is a defect)

- **HC-1 Anti-cheat safety — default ON for every game.** Use only OS-provided, documented user-mode interfaces: ETW consumers (incl. PresentMon), PDH, WMI/CIM, event logs, Windows APIs, and driver-installed vendor libraries loaded dynamically (e.g. `nvml.dll`). Do **not** open handles to game processes beyond the limited-query access those interfaces themselves need (`PROCESS_QUERY_LIMITED_INFORMATION`); no injection, no `SetWindowsHookEx`/hooks, no game-memory reads, no overlays, no drivers. Detect protected titles from the §3.4 catalog (`protected_titles.json`: exe and service/driver names, user-editable). **While any listed process or anti-cheat service is running, anti-cheat-safe mode suppresses** the elevated helper, kernel ETW, WPR, USB analytic tracing, the Deep profile and any other foreign-PID `OpenProcess`; each suppressed collector reports "suppressed (protected title)". The user may override per game after a plain-language warning. Never claim "anti-cheat approved"; state only what the app does *not* do, and that per-title compatibility is the user's to confirm. As of 2026-10-16 no anti-cheat vendor publishes an allowlist or a ban rule for ETW/PresentMon (a search found none; the only documented hard blocks are vulnerable kernel drivers), so the design minimizes risk but cannot guarantee safety — say so in the UI and docs.
- **HC-2 No third-party kernel drivers.** Reject sensor libraries that load drivers (WinRing0/PawnIO-style): HVCI/Memory-Integrity, Vanguard/EAC and blocklists make them unreliable and risky on user machines (WinRing0, CVE-2020-14979, is on Microsoft's default driver blocklist). Prefer vendor user-mode APIs, PDH, WMI, ETW. A library that can run with its driver disabled (e.g. LibreHardwareMonitor ≥ 0.9.5 with Ring0 off) may be evaluated **only in that mode**, driver never installed, affected sensors reported unavailable.
- **HC-3 Least privilege.** Default non-elevated and read-only. Elevation only for a named feature, only in the small separate helper (§6.1), never silently.
- **HC-4 Input privacy.** Hotkeys via `RegisterHotKey` only. Raw Input is used for **timing and rates only** — never key codes, characters or pointer positions. **Keyboard events are reduced to ≥ 100 ms activity-count bins inside the handler; per-key timestamps are never retained or persisted** (inter-key timing is a known side channel for typed text). Pointer devices keep fine-grained timing but never positions. A reviewer must be able to prove from code that typed text cannot be reconstructed.
- **HC-5 No egress by default.** No telemetry, no cloud AI, no report upload, **no automatic update check or download** (update checks, if offered, are opt-in, signed and user-visible). The only other permitted egress is ad content fetched by the sandboxed UI-process ad host under HC-13. Network probes only to user-approved targets at conservative rates (§9).
- **HC-6 Observer effect.** Collection must not cause the stutter it investigates (§14).
- **HC-7 Mutation allowlist.** Changes only via cataloged, allowlisted actions (§12). Never execute generated scripts or strings; never download-and-run binaries.
- **HC-8 Security features are report-only.** VBS/HVCI, Defender, firewall, SmartScreen, Secure Boot, anti-cheat, core services: never disabled and never recommended for disabling. Third-party antivirus scanning game folders is a legitimate stutter cause; any exclusion suggestion is **recommend-only with an explicit security-risk statement**, never automatic.
- **HC-9 Honest provenance.** Fixture/simulated data is tagged at the type level and cannot be exported as a measurement.
- **HC-10 Third parties.** Before adopting any library/tool record license, maintenance status, redistribution terms and limits in `docs/THIRD_PARTY.md` (with an SBOM). No undocumented interfaces, no silent reliance on another product's private service. Licenses verified 2026-10-16 (re-check at adoption): PresentMon MIT; TraceEvent MIT; NVAPI MIT; LibreHardwareMonitor MPL-2.0 (its PawnIO driver is GPL-2.0 with a combining exception); ADLX under its own SDK license agreement (**read it before shipping**); Intel IGCL license not confirmed; NVML has no stated redistribution terms (P/Invoke the driver-installed `nvml.dll`, never ship it).
- **HC-11 Managed or unknown ⇒ Recommend-only** (§3.2, §3.5).
- **HC-12 No hardcoded vendor/model logic** — knowledge lives in validated catalogs (§3.4).
- **HC-13 Ads firewall (§4.1).** Ads exist only in a sandboxed WebView2 ad host inside the **UI process**; never in the Engine or helper; never during a detected game session; never on diagnosis, recommendation or optimization screens; no diagnostic or device data in any ad request; revenue never influences findings; a failed, blocked or offline ad slot never degrades functionality.

---

## 6. Architecture requirements

### 6.1 Process model
- **Engine** (headless, non-elevated): collectors, rolling buffer, correlation, storage; keeps running if the UI closes; UI rendering is never on the capture path.
- **UI** (separate process): talks to the Engine over a local, access-controlled channel; can be closed during play.
- **Privileged helper** (optional, elevated, on demand, signed): exposes only fixed typed commands ("start frame/ETW capture for N seconds", "apply action X with schema-validated parameters"). Launched per use via UAC on a **random-named pipe** created with `FILE_FLAG_FIRST_PIPE_INSTANCE`; accepts **one client whose PID equals its launcher** (`GetNamedPipeClientProcessId`) — an ACL limited to "current user" alone would let any same-user process, including a game, drive it; runs only from an **admin-writable install path** (portable builds refuse elevation); every request schema-validated; **each mutating command needs a single-use approval token minted after UI confirmation**; no strings passed to shells; version-pinned to the Engine; exits when idle; clear UAC reason text.

### 6.2 Time model
One monotonic base clock (QPC). Configure ETW sessions with a QPC-compatible clock where possible; for wall-clock-only sources (event logs, WMI) record periodic QPC↔UTC sync pairs and attach an **uncertainty estimate** to every converted timestamp. Store UTC/ISO-8601 plus QPC. The timeline shows uncertainty and never claims ordering finer than it.

### 6.3 Data model
Fixed-size in-memory ring buffers per signal; SQLite (WAL) for settings, incidents, audit log, benchmarks — schema-versioned with migration tests; one versioned **incident bundle** archive (manifest + signal tables + events + environment snapshot + note). Retention caps by size and age (default ≤ 2 GB total, ≤ 50 MB per incident, **auto-scaled down on low-disk systems**), oldest-first eviction, never silently evicting unexported marked incidents.

### 6.4 Collector contract
One interface: `Probe()` (available? needs elevation? needs tool? why not), `Start/Stop`, health, measured self-cost, back-pressure behaviour, and a declared signal list (units, rate, limitations, tier). Failures are isolated, restarted with back-off and reported — never crash the Engine or leak a trace session. Parsers are **version-tolerant** (e.g. PresentMon CSV/API columns by name; unknown columns ignored; missing columns degrade features). Every signal carries its **source** and an explicit **"not available (reason)"** state (e.g. WLAN error 5, missing PDH instance, disabled analytic channel) — never a fabricated zero. Registry reads that Microsoft documents only informally (HAGS, Game Mode, VRR flags) are best-effort: version-guarded, absent ⇒ "unknown" (never "off"), and replaced by a documented API when one exists.

### 6.5 Capability matrix
Generated **at runtime from `Probe()`**, exportable (§3.6), also documented statically in `docs/CAPABILITIES.md`. For each signal: measured directly / needs elevation / needs optional tool / not measurable by software.

### 6.6 Technology defaults (justify deviations in an ADR)
C# on **`net10.0-windows`** (fail the build if any project targets .NET 8/9); **self-contained** publish (end users have no runtime installed); **WPF** as the default UI — WinUI 3 has no built-in tray icon, no single-file publish and a reported elevation-launch problem on 24H2, so choose it only by ADR with those costs weighed; tray via WinForms `NotifyIcon` or H.NotifyIcon, hotkeys via `HwndSource`; charts via a license-checked library (ScottPlot.WPF is the leading MIT candidate; check .NET 10 compatibility of any alternative); SQLite; xUnit-style tests; TraceEvent (MIT) **pinned to an explicit version** — a major release only days old should not be adopted without reading its notes; NVML via the installed driver's `nvml.dll`. **Frame-capture privilege strategy is a mandatory ADR** evaluating: (a) frame capture hosted in the per-session elevated helper, (b) unelevated capture **only if the user is already in *Performance Log Users*** (detected by actually opening a real-time ETW session; the app never edits groups), (c) PresentMon's own API/service only where its license and documented interface allow — **never install, contact or depend on another product's PresentMon service** (one may already be running), (d) bundling a pinned PresentMon build versus consented, hash-verified download. **PresentMon facts to build on (verified 2026-10-16; re-check):** MIT license (ship the notice); v2.6.0 is current and v2.5.0's release assets were pulled, so **pin an exact version and verify the executable's hash before every launch**; the console exe (`--output_stdout`, `--process_name`, `--session_name`, `--terminate_on_proc_exit`) is the simplest path from .NET; the service/C-API path has no official .NET wrapper and is binary-compatible only with the DLL its own service installs; do not reimplement its PresentData library over TraceEvent; 2.x metrics are referenced to CPU frame start and are not comparable with 1.x logs; without admin, other users' or short-lived processes appear as `<unknown>`. **PresentMon rules:** use a unique session name (e.g. `FpsIntel-<pid>`); `--stop_existing_session`/`--terminate_existing_session` only for an orphan carrying our own prefix; add a capture watchdog and a one-click stop (one case report describes a game becoming unresponsive under PresentMon capture — single and unverified). Verify PresentMon's current license, privilege requirements and API stability against official sources before deciding. `wpr.exe` ships with Windows for optional deep captures; WPA is an optional external viewer, never a dependency.

---

## 7. Priority One — FPS instability and stutter

### 7.1 Metrics and operational definitions (document in `docs/METRICS.md`)
- Per-frame: frame time, display-change interval where available, CPU busy / GPU busy where available, present mode, API, dropped/lost-event counts.
- **Frame generation (DLSS/FSR/AFMF):** `MsBetweenPresents` (application frames) and `MsBetweenDisplayChange` (displayed frames, which include generated ones) diverge — report both, label which one every FPS and low figure uses, and never mix them.
- **Average FPS** = frames ÷ duration (not the mean of per-frame FPS).
- **1% / 0.1% low:** one primary definition (default: FPS at the **99th / 99.9th percentile frame time**), labeled everywhere, with the alternative (mean of slowest 1%/0.1%) as a secondary column. 1% low needs ≥ 1 000 frames in the window; **0.1% low (≥ ~10 000 frames) is shown for session-level windows only** — incident windows show "insufficient samples".
- **Stutter event** (default, adjustable, "uncalibrated" until baselined): frame time ≥ 2× the rolling 1-second median **and** ≥ 4 ms above it. Report count, duration, severity, clustering. Always distinguish *low average FPS* from *isolated spikes*.
- Outliers are never silently dropped; offer with/without views for alt-tab and loading windows when detectable. Lost events, collector gaps and clock gaps are timeline entries and **lower the grade** of overlapping findings.
- System context at 1–10 Hz (faster only during incident capture): CPU total/per-core (topology-aware), effective frequency, GPU engines per adapter, VRAM, vendor clocks/power/thermal/throttle reasons when available, memory (available, commit, hard faults/s), disk latency/queue, top background-process deltas.

### 7.2 Game and display awareness
- **Any game, any API** (DX9–12, Vulkan, OpenGL, Store/UWP, launchers, compatibility layers). **Game detection by consent:** when a process is observed presenting frames full-screen or at sustained high rate, *propose* "add as a game?" — never scan launcher libraries or user files to guess.
- Identify **which adapter renders** each game, **which adapter drives the display**, windowed vs borderless vs exclusive, flip-model status, refresh rate per monitor, and **mixed-refresh multi-monitor** and VRR configuration (all read-only). Report conditions such as "game on iGPU", "dGPU rendering via iGPU-routed display", "second monitor at a different refresh rate".
- Record AC vs battery (when present), Windows power mode/plan, and vendor thermal/GPU mode **only from documented interfaces** — otherwise show "inferred from output topology" or "not exposed" (undocumented OEM WMI is excluded by HC-10). **Refuse to compare benchmarks across different power states.**
- Throttling is graded only from direct indicators (vendor throttle reasons, firmware-limit events, effective-frequency collapse), never from temperature alone.
- **Do not wake sleeping GPUs:** on battery, or when nothing renders on a discrete GPU, do not call vendor APIs (e.g. NVML) that may keep it out of its low-power state; poll PDH only and record the GPU's power state.
- PDH GPU counter instance names embed `pid`, `luid` and `engtype`, and hybrid machines return several adapters — always key by LUID, never by index.

### 7.3 Incident capture
- Rolling buffer, default pre-roll **120 s** (30–600 s configurable), post-roll **30 s**; buffer sizes scale to hardware class.
- Configurable global hotkeys (defaults e.g. `Ctrl+Alt+F9` stutter, `Ctrl+Alt+F10` input stall). **Feedback without overlay:** audible cue plus tray state; **handle `RegisterHotKey` failure** (another product may own the chord; F-row keys depend on Fn-lock), show the active chord in the tray, and offer a tray "Mark" fallback.
- **Human reaction delay:** the key press is *not* the event time. On mark, search the preceding ~10 s for candidate anomalies (largest frame spike, input gap, device event) and offer them as candidate anchors; the user picks/adjusts. Never silently align to the key press.
- Optional note; *Passive* vs short *Deep* profiles (Deep shows a gameplay-impact warning and is blocked for protected games per HC-1).
- Auto-flagged stutters are **unconfirmed candidates**, clearly distinct from user-marked incidents. Bundles are written at **low I/O priority**, and auto-flags are limited to 1 per 5 minutes and 10 per session so they cannot create the I/O bursts they investigate.

### 7.4 Cause investigation
Per hypothesis: direct evidence, supporting evidence, disqualifiers, targeted test. Starting set: CPU main-thread bound vs core saturation; **CPU topology/scheduler placement (hybrid P/E, multi-CCD/X3D)**; GPU bound; rendering stalls and driver resets (TDR, vendor driver events); thermal/power limiting; wrong-GPU/hybrid path; VRAM exhaustion; RAM pressure and hard-fault bursts; storage latency and device resets; shader compilation/asset streaming (only if distinguishable); DPC/ISR (needs Tier C); overlays/capture/recording; **third-party AV real-time scanning**; frame cap/VRR/V-Sync/refresh mismatch and mixed-refresh monitors; background bursts (indexing, sync, updates, vendor utilities). Validate every Windows event ID against Microsoft documentation before use.

---

## 8. Priority Two — wireless mouse, input and USB lab

A **dedicated workflow**, vendor-neutral. The Razer dongle mouse is the reference validation device only.

- **Device picker:** enumerate pointing devices (any mouse, touchpad, pen, controller-as-mouse); confirm the target with a "move it now" Raw Input test. Never guess the target.
- **Passive evidence:** USB/HID arrival/removal/problem events (Kernel-PnP configuration log — IDs 400 configured, 410 started, 411 problem starting, 420 deleted, 430 needs further installation; System 219 is usually benign; USBHUB3 events in the System log are power-related and disconnect/reset detail exists only in the high-volume Analytic channel; USBXHCI-Operational may help — re-verify IDs against Microsoft docs), device-change notifications via `CM_Register_Notification` (no window needed; it does not report existing devices, so enumerate after registering), PnP problem codes, controller/hub topology and shared ports, plan-level and **per-device** power-management flags (read separately), driver versions, and presence of peripheral/RGB/OEM suites from the catalog.
- **Raw Input timing:** inter-arrival histogram per device, stamped with QPC immediately after `GetRawInputBuffer` (headers carry no hardware timestamp); report the **gap distribution — median, p99, max — not the mean**, because mice report only while moving and batching distorts rates; estimated report rate; **input-gap detection**. Background capture needs `RIDEV_INPUTSINK` plus a target window — test behaviour while an elevated window (a game run as admin) has focus (UIPI) and document the result. State plainly this measures *delivery to Windows*, not RF signal, radio retries or physical latency. Surface very high report rates as a *hypothesis* about input-processing CPU load, with a recommend-only test (lower the device's polling rate in its own software).
- **Differential diagnosis (core):** use the **keyboard and any second pointing device (e.g. touchpad) as control devices**. For a marked stall:
  - target mouse gap **while** keyboard/other devices, frame pacing and scheduler health stay normal → grade toward *device / receiver / link / USB-power* causes;
  - mouse gap **coinciding with** frame spikes, other-device gaps or system latency → grade toward *system-level stall* (CPU, DPC/driver, storage, thermal);
  - otherwise **inconclusive**, with the next isolating test offered. Desktops without a touchpad use the keyboard alone and say so.
- **Control-validity rules (mandatory):** a control device counts only if it produced ≥ 5 events/s in the 2 s before **and** after the gap — keyboard activity is intermittent in games, so silence is *no data*, not "normal". Without a valid control the verdict is capped at **Inconclusive**. Raw Input headers carry no hardware timestamp, so the handler runs on a **dedicated high-priority, allocation-free thread that stamps QPC at dequeue and logs its own wake latency**; gaps overlapping the app's own scheduling or GC stalls are discarded. Frame pacing and an always-on timer-latency probe are the continuous system-level controls.
- **The input marker must be keyboard-triggered** (the mouse may be what froze), with audible confirmation.
- **Guided one-variable tests** with expected outcomes under each hypothesis and a persisted result: different USB port (preferably another controller); receiver on a short extension away from USB 3.x devices and the Wi-Fi/Bluetooth antenna area; Bluetooth vs receiver (if supported); per-device power management off; closing vendor/RGB suites for one session; wired control session.
- Battery/signal only where the device exposes them through a **documented standard interface** (e.g. HID battery usages or the Bluetooth LE Battery Service); community-known undocumented PnP properties are excluded by HC-10. A 2.4 GHz dongle mouse such as the reference Razer has no standard path, so its battery and signal read **"not exposed to software"** — never reverse-engineered vendor reports.
- Optional elevated USB analytic tracing: off by default, time-boxed, with overhead disclosure, blocked for protected games per HC-1.

---

## 9. Priority Three — network and online play

- Targets: local gateway (auto), configured DNS resolver(s), user-chosen internet hosts, optional user-entered game-server addresses. Probes are user-approved, **≤ 2 probes/s per target, ≤ 10 targets, explicit timeouts**, visible activity indicator, no flooding, no packet capture by default. ICMP uses `IcmpSendEcho` (no admin needed) with a clearly labeled TCP-connect fallback for hosts that filter ICMP.
- Metrics: RTT (min/median/p95/max); **jitter** (default: mean absolute difference of consecutive RTTs; also expose RFC 3550's smoothed estimator `J += (|D| − J)/16` — label which one is shown); loss with confidence (an unanswered ICMP is **not** proof of network loss — ICMP may be filtered or deprioritized); DNS latency/errors per resolver; adapter link events; Wi-Fi band/channel/signal/rate/roaming via the **WLAN API** — on current Windows 11 builds these calls return access-denied without **Location consent**, so `Probe()` then reports "needs Location permission (user action)", the app never changes that setting, and Wi-Fi conclusions (including Wi-Fi-vs-Ethernet) are withheld without it; retry/error fields in `WLAN_STATISTICS` are shown only if the driver populates them; Ethernet link speed and counters.
- Classify fault location (*local device / Wi-Fi-or-router / upstream / remote / inconclusive*) by comparing gateway vs DNS vs internet series, showing the evidence.
- State the ICMP-vs-game-traffic (UDP) limitation wherever latency appears. Record which adapter each sample used so Wi-Fi vs Ethernet A/B is possible. Note VPN/proxy presence.
- **Bufferbloat:** only via an explicit, user-started, controlled latency-under-load test against a documented target, disclosing bandwidth used and any third-party terms. Never inferred from a speed figure. Method: idle RTT vs RTT under load (the Waveform approach; the IETF "Responsiveness" work is still a draft). No official terms were found for automated use of Cloudflare's speed endpoints, so the target is **user-configurable** (self-hosted LibreSpeed is an option — verify its license file, as listings conflict) and nothing runs without visible consent.
- Network setting changes are proposals only (problem, expected effect, risk, original value, rollback). No DNS/TCP/MTU "boosts".

---

## 10. Supporting system health

Drivers and events (display, storage, USB, Wi-Fi, WHEA, power) with recent-change correlation. Driver recommendations only with a concrete reason and an **official vendor source**; no automatic downloads/installs. Power/thermal/firmware indicators are read-only; no clock/voltage/power-limit/BIOS changes. Display configuration is inspected and recommendations are per-game and per-hardware. Background interference is reported; processes are never killed blindly.

---

## 11. Diagnosis engine

- **Deterministic rules first.** Each rule in `docs/RULES.md`: id, symptom, required signals, minimum samples, thresholds *and how derived*, control comparison, counter-evidence checks, output grade, user text, and **three fixtures** (positive, negative, ambiguous), including at least one from a non-reference configuration.
- **Thresholds are relative to the user's own baseline** (per game, per power state: median and MAD from earlier clean sessions). Until a baseline exists, constants are labeled **"uncalibrated"** and cap the grade at *Possible*.
- **Control windows:** compare each incident window with matched control windows from the same session and prior sessions. A process or event equally present in controls is **not** evidence.
- **Grades:** **Confirmed** = a direct-evidence signal inside the uncertainty-adjusted window, no contradicting signal, no overlapping data gap. **Strongly supported** = ≥ 2 independent supporting signals beyond control levels. **Possible** = plausible, needs a named test. **Inconclusive** = evidence cannot separate alternatives. Time proximity alone never raises a grade.
- **Every recommendation card shows:** problem; observations with timestamps/uncertainty; hypothesis; grade; limitations; next step; expected benefit and downside; permissions; exactly what would change; verification; rollback.
- **History/"learning" is statistics, not ML:** recurrence counts, per-fix before/after intervals, and a *"fix stopped helping"* alert.
- **Optional local AI** may summarize an already-computed, redacted structured result; no tools, no write access, no new numbers, visibly labeled. **Cloud AI is off by default**; enabling needs consent screens listing exact fields sent, a payload preview and per-use confirmation.

---

## 12. Safe optimization system

**Modes:** *Diagnose* (read-only) · *Recommend* (explain only) · *Optimize* (apply one individually approved, evidence-backed action). Managed/unknown configurations are locked to Recommend (HC-11).

**Catalog entry (all mandatory):** id; eligibility preconditions **expressed as detected configuration**; problem addressed; required evidence (rule ids + minimum grade); supported Windows/hardware; risk and reversibility class; exact change plan; original-value capture; error handling; post-change verification; rollback and **rollback verification**; reboot needed?

**Crash-safe protocol:** write a *pending* journal entry (original + intended value) **before** changing anything; apply; verify; mark *applied*. On startup reconcile *pending* entries (finish verification or roll back). Risky settings use a **confirm-or-auto-revert timer**. Uninstall offers to roll back all active changes. A failed or partial rollback is reported as such — never as success.

**Seed candidates (build only after each is validated; start with 2–3):**
1. Per-app GPU preference → *High performance* for a game exe that evidence shows rendering on the integrated GPU (user-level, no admin, trivially reversible). Capture the **entire** `UserGpuPreferences` value string (it may hold other tokens) and verify at the next game launch via the PDH adapter LUID.
2. Per-device USB power-management off for the input receiver path (needs USB removal/reset evidence; may need admin). Confirm-or-revert uses the **keyboard** (the mouse may be dead).
3. Windows power mode/plan for AC play when effective-frequency evidence shows clocks held down (never on battery without explicit consent). Requires **no thermal/power-limit throttle indicator**; verification re-reads the effective mode after 5 minutes and after reboot, and a vendor-utility revert is reported as "overridden".
4. Game Bar / background recording off when a capture workload is evidenced during incidents.
5. Wi-Fi power saving via the active power plan's *Wireless Adapter Settings → Power Saving Mode* (subgroup `19cbb8fa-5279-450e-9fac-8a3d5fedd0c1`, setting `12bbebe6-58d6-4636-95bb-3217ef867c1a` — verify with `powercfg /qh`) when jitter and adapter evidence support it. The driver-registry route needs an adapter restart (drops the connection, breaks the revert timer) and is **recommend-only**.
HAGS, VBS/HVCI, drivers, firmware and reboot-required changes are **recommend-only** until separately validated and explicitly confirmed.

**Banned:** RAM/standby-list "cleaners", cache wipes, page-file disabling, blanket priority/affinity loops, timer-resolution hacks, EcoQoS/efficiency-mode or priority changes on foreground game processes (Microsoft advises against EcoQoS for foreground work), killing arbitrary tasks, mass registry tweaks, disabling Defender/firewall/services/anti-cheat/VBS, TCP/DNS/MTU "boost" tweaks.

One-click rollback per action and an **audit history** (previous value, new value, timestamp, result, failure detail).

---

## 13. Gaming profiles and controlled automation

Per-game profiles hold collection preferences, hotkeys, thresholds/baselines, protected-title flag and approved actions. Start/stop monitoring on game launch/exit using process-start detection that never touches the game process. Allowed automation: alert on a recurring fault, save an incident, apply a previously validated reversible profile — **re-applying only actions already individually approved, under a per-profile standing opt-in recorded in the journal, limited to user-level (no-UAC) actions** — and restore original state when the session ends. Every automatic change is opt-in, visible, rate-limited (no continuous manipulation on noisy readings) and audited.

---

## 14. Self-overhead and self-protection

- **Initial budgets (measure, then revise with evidence):** passive monitoring ≤ **1% of total CPU capacity** on ≥ 8 logical processors (≤ 2% on smaller CPUs), measured by summing **per-process counters over all app-owned processes** (Engine, helper, any PresentMon child) over ≥ 10 minutes; Engine working set ≤ **200 MB** (scaled down on ≤ 8 GB RAM systems); average disk writes ≤ **5 MB/min**; no hot-path allocations that cause GC pauses.
- **Hardware-class scaling:** low-core / low-RAM / HDD / battery / handheld systems automatically use lower sampling rates, smaller buffers and fewer collectors; monitoring cost on battery is measured and shown.
- **Observer-effect test (mandatory):** paired runs of the same workload with collectors on vs off, measured by the **harness logging its own QPC at `Present` return** (independent of the app's capture), reporting frame-time differences with intervals.
- **Self-throttle:** if own CPU/IO/memory/GC exceeds budget or own threads appear in incident windows, reduce or stop optional collectors, warn, and tell the user. Detect and release failed collectors and orphaned trace sessions (only those the app created).
- Before any ETW session, enumerate existing sessions (`logman query -ets` or the API — the system limit is 64 and machines may already run ~40, sometimes including orphans from crashed tools). Name every session with a **unique app prefix**, stop on dispose, and stop orphans carrying **our** prefix at startup; never stop sessions we did not start — report foreign orphans instead. **Never start the NT Kernel Logger or any system-logger session while another tool owns it** (detect and report; system providers can feed up to 8 loggers with the first two reserved, and a non-default name needs `EVENT_TRACE_SYSTEM_LOGGER_MODE`). Avoid duplicate PresentMon/ETW consumers (another product's PresentMon service may already be present).

---

## 15. User interface and first run

Modern, coherent design system; dark and light themes; **DPI 100–250%**, multi-monitor aware, keyboard-navigable, screen-reader labels, accessible contrast, localizable. **First-run flow:** plain-language purpose, what is and isn't collected, permission table, anti-cheat statement (HC-1), ad disclosure (§4.1), choice of Diagnose/Recommend/Optimize (default Diagnose), hotkey setup and a conflict check — all skippable and revisitable.

Workspaces: **Overview · Live Monitor · Stutter Investigator · Mouse & USB Lab · Network Lab · Drivers & System Health · Optimization Center · Benchmarks · Gaming Profiles · Reports & Settings.** Plain-language summary by default; raw traces and rule internals behind "Advanced". No fake health scores, decorative metrics, all-green checkmarks that hide unresolved items, or in-game overlay. Every number shows unit, window and data-quality flag; unavailable data reads "not available — reason".

---

## 16. Privacy, security and distribution trust

- Local-only by default; documented permission table (what, why, when).
- **Exports:** redaction ON by default for Windows username in paths, hostname, Wi-Fi SSID/BSSID, MAC/IP, device serials and process command lines; **preview before export**. WPR/ETL traces contain command lines, paths and usernames, cannot be reliably redacted and are GB-sized, so they live **outside** the incident bundle, are labeled *unredactable*, and are excluded from export unless the user ticks a separate warning. Redaction is tested with **canary strings** (username, hostname, SSID, MAC, IP, serial) planted in fixtures; export **fails closed** if any survives.
- No browser data, documents or credentials are read. Process names/device IDs collected only as needed.
- `docs/SECURITY.md` threat model: helper IPC spoofing, argument injection, malicious report import, tampered journal, supply chain (pinned, license-checked dependencies; no auto-downloaded binaries), catalog tampering (schema-validated, data-only).
- **Distribution plan:** self-contained build, **code-signing plan** (Smart App Control blocks unsigned apps outright when on; SmartScreen reputation also applies — signing is a release requirement, not polish), installer choice by ADR (clean install/uninstall, per-user install preferred), no bundled third-party offers or pay-per-install monetization, opt-in signed update mechanism or manual updates. Draft EULA/privacy notice marked **"needs legal review"** that **discloses the ad requests** (§4.1) (applicable privacy law varies by region, including rules on advertising to minors; this is not legal advice).
- Fail closed: if a permission or integrity check fails, do nothing and say why.

---

## 17. Delivery stages and exit criteria

**Stage 0 — Bootstrap:** install the pre-approved toolchain only (§2.2 #3); probe **Smart App Control** (`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState`) — if On/Evaluation, unsigned self-built executables (including the helper) may be blocked: **stop and ask, never disable it** (HC-8); repo layout; one-command build/test script; ADRs (including the §6.6 frame-capture ADR); capability-probe prototype; **synthetic workload harness** and **compatibility-fixture framework** skeletons; the **monetization feasibility ADR** (§4.1 — research only, no ad code) and the **cloud-test runbooks** (§18). *Exit:* clean checkout builds and tests via one documented command.

**Stage 1a — Headless core:** hardware/GPU/topology discovery; runtime capability matrix; frame collection per the ADR; PDH + vendor-optional system context; metrics; ring buffer; C# synthetic harness; CLI incident report; overhead measurement v1; standard-user run. *Exit:* injected hitches in the harness detected within ±1 frame (agent-verified).

**Stage 1b — Interactive slice:** hotkey marker (with registration-failure handling and a tray "Mark" fallback), candidate-anchor search, timeline UI, redacted export with preview. *Exit:* **STOP for user validation [U]** on a real, non-protected game session (`docs/VALIDATION_1.md`).

**Stage 2 — Input and network:** Raw Input timing, USB/PnP events, differential diagnosis, guided tests, network probes and history, correlation with incidents. *Exit:* fixtures + a live unelevated run with honest "not exposed" entries.

**Stage 3 — Expanded diagnosis:** optional elevated helper (kernel ETW DPC/ISR), driver/WHEA/storage rules, power/thermal, WPR capture, baseline calibration, catalog expansion. *Exit:* rules documented with fixtures (incl. non-reference configs); helper security review passed.

**Stage 4 — Controlled optimization:** journal, approval flow, 2–3 validated actions, verification and rollback incl. failure-injection tests, benchmark module. *Exit:* **STOP for user validation [U]** of before/after.

**Stage 5 — Hardening and release:** 24 h passive soak (no leaks, bounded storage), self-contained package/installer, signing plan, install/uninstall docs, privacy doc, **ad integration per §4.1 (only if the ADR found a compliant network; behind a feature flag)**, **cloud-VM compatibility campaign (§18)**, `COMPATIBILITY.md`, external beta plan, limitations register, final report.

Continue through the stages; the MVP is not the finished product. If genuinely blocked, document what is done, what remains, and the exact blocker.

---

## 18. Testing strategy

- **Pyramid:** many unit tests (metric math, percentile definitions, rule logic, journal, redaction, time conversion); integration tests (collector → buffer → bundle → report; helper IPC); few end-to-end/UI smoke tests. Targets: ≥ 90% lines on rule engine, journal/rollback, statistics, redaction, catalog validation; no coverage chasing in UI code. Hardware-dependent tests are **tagged and skipped with a reason** on machines lacking the hardware, so the suite runs on any Windows PC or VM.
- **Synthetic ground-truth harness:** a small DXGI/Direct3D app that renders at a configurable rate and **injects known hitches**, validating capture, stutter detection, lows and incident anchoring without a real game or anti-cheat. Bounded CPU/GPU/disk stress generators (capped duration, temperature guard) exercise rules; synthetic Raw Input/USB/event-log replay fixtures. All tagged **synthetic**.
- **Portability tests (new):** localized/foreign counter names → English paths still resolve; event handling by provider+ID under non-English message text; PresentMon output of different versions and with missing/extra columns; **absent** NVIDIA/AMD/Intel vendor APIs; no GPU counters; no battery (desktop); multi-monitor mixed refresh fixtures; Windows 10 feature-detection paths; HDD and ≤ 4-core hardware-class scaling; **standard-user vs admin parity**; managed-device and VM simulations forcing Recommend-only; catalog schema validation and "no hardcoded vendor logic" check.
- **Cloud compatibility testing (user decision: free tiers).**
  - *What cloud VMs can validate:* Windows version/edition/language matrix, standard-user vs admin parity, installer/uninstaller and self-contained package, unit/integration/portability suites, the VM-detection path, no-GPU/no-battery/low-core/low-RAM code paths (small VM sizes), AMD vs Intel host CPUs, and present-event capture from the harness running on the software renderer (WARP) — which proves the pipeline, not GPU timing.
  - *What they cannot validate (stay "untested" in `COMPATIBILITY.md` unless the user supplies hardware):* real GPUs and vendor APIs, game frame pacing, anti-cheat behaviour, USB/HID/Bluetooth/Raw Input hardware, Wi-Fi, laptop battery/thermal/hybrid-GPU behaviour, perceived stutter.
  - *Account and cost rules:* the user creates and owns the accounts; the agent never creates accounts, enters payment details or accepts terms. Free offers typically need a card, allow one free account per customer, expire (credits ~30 days, then pay-as-you-go), cap compute cores, and generally need a paid upgrade plus a quota request for GPU VMs — **verify current terms before relying on any of it**. Never provision a GPU SKU or anything outside the free allowance without the user approving the stated cost.
  - *Guardrails:* a budget alert at the lowest available threshold, auto-shutdown schedules, deallocate when idle, delete the resource group after each campaign, resource tags, no secrets in the repo, and runbooks/IaC the user reads before running. Record the evidence of each guardrail.
  - *Licensing:* Windows 10/11 client images on Azure are offered for dev/test on free-trial subscriptions per Microsoft's guidance — **verify the licensing for the user's subscription before use**; otherwise use Windows Server images as a compatibility proxy and label them "not a supported product OS". Windows 365 trials need a card and are time-limited.
  - *Complement (no cloud, no cost):* Microsoft evaluation ISOs in local VMs (including many display languages) — requires the user's approval to install a hypervisor; Home edition has no Hyper-V host.
  - All cloud results are labeled **cloud-VM** in `COMPATIBILITY.md`, never merged with physical-hardware results.
- **Failure paths:** missing sensors, denied permission, interrupted/duplicated traces, collector crash, device disconnect, DNS/ICMP timeouts, clock jumps, disk-full, corrupted bundle, journal crash mid-change, rollback failure, helper absent.
- **Fault-injection layer** around every system-mutating call (Home edition has no Sandbox/Hyper-V; real mutations are tested via the abstraction plus a few manual, user-approved live checks).
- **Benchmark protocol (every before/after claim):** hold everything constant (power mode, vendor profile, GPU path, AC/DC recorded) **except the variable under test**, which is exempt from the equality check (e.g. power mode for seed #3, GPU path for seed #1); benchmarks run only on the **harness, a non-anti-cheat built-in benchmark, or a user-driven fixed route — the app never generates game input**; ≥ 10 runs per condition, **interleaved A-B-A-B**; discard warm-up; cooldown to baseline temperature; report median and IQR plus a **permutation test / bootstrap 95% interval** on the difference in P99 frame time and 1% low (n = 5 is too few for a reliable bootstrap); **improved** only if the interval excludes zero and exceeds a pre-declared minimum effect (default: 5% or twice the run-to-run coefficient of variation, whichever is larger); otherwise **inconclusive** (anti-cheat titles with no repeatable workload report "inconclusive" unless the effect exceeds run-to-run variation); if worse, recommend revert.

---

## 19. Acceptance criteria — tagged by who can verify

**[A]** agent-verifiable · **[U]** needs the user at the PC · **[A+U]** both.

1. [A] Hardware, drivers, topology (incl. hybrid GPU, form factor) and unavailable telemetry are reported accurately; the runtime capability matrix matches the probe.
2. [A+U] A real session is monitored with no fabricated metrics; harness truth by [A], first real run by [U] (non-protected title first; protected titles only after the user opts in).
3. [A] An incident preserves configured pre-roll and post-roll, with candidate anchors and uncertainty.
4. [A] A timestamped report exports with redaction and preview.
5. [A] Input and network views separate **measured facts**, **inferences** and **hypotheses** and never imply RF/physical-latency measurement that does not exist.
6. [A] Every modification is blocked without preconditions and explicit approval.
7. [A] Each shipped action has tested apply, verify, rollback and rollback-failure behavior (fault injection); [U] one live apply/rollback per shipped action.
8. [A+U] Before/after results follow §18 and disclose uncertainty.
9. [A] Overhead is measured per §14 and documented with method, numbers and budget misses.
10. [A] The app stays useful when PresentMon, vendor APIs, elevation or any optional tool is missing.
11. [A] Build, tests and the self-contained package are verified on a clean checkout/profile as far as the environment allows; signing and SmartScreen behavior are documented unverified if untested.
12. [A] The final report separates working-and-verified, working-but-unverified, partial and not-done, matching `docs/STATUS.md`.
13. [A] A **static scan** finds no `SetWindowsHookEx`, `Read/WriteProcessMemory`, `CreateRemoteThread`, driver loading or foreign-PID handle beyond `PROCESS_QUERY_LIMITED_INFORMATION`; and a test with a protected-title fixture shows every HC-1 collector reporting "suppressed".
14. [A] Input telemetry cannot reconstruct typed text (reviewed and unit-tested).
15. [A+U] **Core features run as a standard user with no elevation**; elevated features are offered, explained and degrade cleanly when declined or when no admin rights exist.
16. [A] Simulated no-NVIDIA, no-GPU-counter, desktop (no battery), non-English-locale, Windows 10, HDD/low-core, managed-device and VM configurations behave per §3 (correct degradation, Recommend-only where required).
17. [A] A check proves no vendor/model/OEM names are hardcoded in product logic outside validated catalogs.
18. [A] Catalogs are schema-validated, data-only and each entry has a source and review date.
19. [A] The Compatibility Report contains no personal data and is previewable before export.
20. [A] `COMPATIBILITY.md` honestly lists what was tested (reference machine only, unless more is obtained), what is expected to work, and what is untested.
21. [A] The monetization ADR exists, cites current network terms, and either approves a compliant network or reports that none exists; no ad SDK is added before it.
22. [A] No Engine or helper code path contacts an ad host (static check + network test); ad requests originate only from the UI-process ad host.
23. [A] With canary strings planted in diagnostic data, a captured ad-request log contains none of them.
24. [A] Ads never render on diagnosis/recommendation/optimization screens or during a detected game session; the ad host is disposed when the UI is hidden or a game runs; ad activity is inside the §14 budgets and measured.
25. [A+U] The app is fully functional offline and with ads blocked; [U] the user completes ad-network, tax/payout, privacy-policy and store-policy tasks.
26. [A] `COMPATIBILITY.md` separates physical-hardware, cloud-VM and untested rows, and every cloud run records its cost-guardrail evidence (budget alert, shutdown, teardown).

---

## 20. Deliverables

Source code; automated tests; runnable self-contained build and (Stage 5) installer; and `docs/`: `ARCHITECTURE.md` + ADRs, `SUPPORTED_WINDOWS.md`, `COMPATIBILITY.md`, `THIRD_PARTY.md` (licenses + SBOM), `CAPABILITIES.md`, `CATALOGS.md`, `MONETIZATION.md` (ADR, network-terms review, guardrail tests), `CLOUD_TESTING.md` (runbooks, cost guards), `METRICS.md`, `RULES.md`, `OPTIMIZATIONS.md`, `ROLLBACK.md`, `TESTING.md`, `INSTALL.md`/uninstall, `PRIVACY.md`, `SECURITY.md`, `LIMITATIONS.md`, `RELEASE_NOTES.md`, `STATUS.md`, and an external-beta test plan.

**Final report format:** what was built and verified (with evidence); built but unverified; partial or missing; measured overhead; tested vs untested hardware/OS matrix; known limitations; decisions the user still owns.

---

## 21. Final operating instructions

Work autonomously and inspect before assuming. Build and validate the diagnostic system first; **do not apply optimizations to the user's PC until Stage 4**, and then only individually approved ones. Do not claim hardware capabilities that are unavailable, an FPS improvement that was not measured, a root cause the evidence does not support, or compatibility with a configuration that was not tested. Do not trade stability, security or reliability for a benchmark. The objective is a tool that finds the real cause of intermittent FPS drops, input stalls and network instability on **any** Windows gaming PC — and proves whether its own recommendations help.
