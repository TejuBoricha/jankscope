# Jankscope: competitor and landscape review (as of 2026-11-12)

Method: WebSearch (extended mode for recent/niche facts) and WebFetch on official pages where they were reachable. Every claim carries a source URL. Tags:
- [F] = read from a fetched page (official unless the URL says otherwise)
- [S] = from a search-result summary only, not a fetched page (secondary; treat as lightly verified)
- [U] = unverified / from model memory; no page backs it

Nothing in the Jankscope repo was read, modified, committed or pushed. No software was installed. No repo content, email or usernames were sent to any service.

## What could NOT be verified (read this first)
- hwinfo.com (home, download, version-history) and guru3d RTSS page returned HTTP 403 to WebFetch. HWiNFO and Afterburner/RTSS facts are from search summaries and Microsoft's page only [S].
- No official Easy Anti-Cheat (EAC) statement about third-party monitors or ETW was found. EAC claims below are community-level [S].
- No official Vanguard page lists "allowed" or "blocked" tools. Riot's support page only says it shows the offending driver path [F].
- Razer Cortex price and telemetry: official pages were found by search only; razer.com fetches 404'd. Premium pricing not verified.
- Windows Game Bar / Task Manager: no official Microsoft feature page was fetched; only community and search summaries.
- Whether any specific anti-cheat blocks ETW providers is not documented in any source found. Only PresentMon's own 2.5.0 note hints at it (see Risks).
- Download counts, user numbers, market share: not found; none are stated here.
- Jankscope's own capabilities are taken from the task brief, not re-checked against the repo.

## Per-tool review

| Tool | What it does | Price / licence | Kernel driver / injection | Anti-cheat friction | Telemetry / ads | UX strengths |
|---|---|---|---|---|---|---|
| **HWiNFO** (v8.60 listed 2026-11-10 [S]) | Deep hardware sensor monitor | Free for personal non-commercial use; Pro paid, third-party site says from $29/yr or $199 lifetime (unverified price) [S] https://www.techspot.com/downloads/1238-hwinfo.html , https://www.hwinfo.com/version-history/ (403 on direct fetch) | Uses a kernel driver (HVCI-compatible update mentioned) [S]. Named by Microsoft as affected by the WinRing0 vulnerable-driver alert [F] https://support.microsoft.com/en-us/windows/security/threat-malware-protection/microsoft-defender-antivirus-alert-vulnerabledriver-winnt-winring0 | HWiNFO forum says the Vanguard conflict is Vanguard blocking drivers, not HWiNFO's fault [S] https://www.hwinfo.com/forum/threads/incompatibility-with-riot-vanguard-anti-cheat.6356 | Not found | Most complete sensor list; sensors feed other tools |
| **MSI Afterburner + RTSS** (4.6.6 stable Sep 2025 with RTSS 7.3.7; 4.6.7 betas 2026 [S]) | OC/fan control, on-screen overlay, frame limiter, capture | Free [S]; RTSS freeware [F] https://en.wikipedia.org/wiki/RivaTuner | RTSS hooks into game rendering (overlay + limiter) [U for mechanism detail; the Wikipedia fetch did not describe injection]. Afterburner has an optional "low level IO driver" [S]. Named on Microsoft's WinRing0 list [F] | Vanguard has blocked the Afterburner IO driver (RTCore64.sys) since ~2020; fix is disabling the low-level IO driver [S] https://forums.guru3d.com/threads/rtcore64-sys-and-valorant-vanguard.431963/ . BattlEye FAQ: non-cheat overlays "generally supported" unless a game blocks them; tells users to uninstall MSI Dragon Center / MSI SDK [F] https://www.battleye.com/support/faq/ | Not found | Familiar overlay and fan/OC workflow; huge community |
| **CapFrameX** (v1.9.1 stable 27 Sep; 1.9.2 beta Oct 2026) | PresentMon-based capture + analysis, comparisons, overlays, cloud sharing | Free download; licence text not stated on page [F] https://github.com/CXWorld/CapFrameX , https://github.com/CXWorld/CapFrameX/releases | Bundles and manages the WinRing0 driver for its PMC sensor reader [F releases]. Microsoft names CapFrameX in the WinRing0 alert [F] | Release notes tell users to fully exit hypervisor-based kernel anti-cheat (Vanguard) if sensors show nothing [F releases]. README has an "anti-cheat pattern" for failed captures [F] | Cloud upload/download only when used; process-list option can add game names to an online list [F]. Has a local MCP server (v1.8.5+), bound to localhost with no auth, with start/stop capture tools in v1.8.6 [F releases] | Strongest analysis (percentiles, frametime charts, compare runs, reports) |
| **Intel PresentMon** (v2.6.0, 2026-09-21) | CLI, service, capture app, configurable overlay, GPU Busy, SDK; v2.6.0 adds Game Experience overlay preset, D3D12 PSO compile tracking, lower CPU overhead [F] https://game.intel.com/us/intel-presentmon/ , https://github.com/GameTechDev/PresentMon/releases ; [S] https://www.techpowerup.com/353118/intel-presentmon-2-6-0-update-slashes-cpu-usage-adds-game-experience-overlay | Open source, MIT, copyright Intel [F] https://github.com/GameTechDev/PresentMon . Price not stated (free) | ETW-based collection, no hook for frame data [F]; needs Performance Log Users group or admin [F]. Overlay and GPU telemetry via vendor APIs (e.g. NVAPI) [F] | Not mentioned on pages [F]. 2.5.0 notes say changes aim to collect data from some titles that block ETW tracing [F releases] | Intel privacy notice linked, no data detail [F] | Official engine everyone else builds on; new GPU Busy and PSO-compile metrics |
| **NVIDIA FrameView** (v2.0 current per product page; 1.7 in Jan 2026 per [S]) | Frame time, FPS, power, perf/W, Reflex PC latency; overlay + log | Price not stated on page (free) [F] https://www.nvidia.com/en-us/geforce/technologies/frameview/ | Built on PresentMon [F]. PCAT is a separate hardware power-capture device, needed for accurate AMD board power [S] https://images.nvidia.com/content/geforce/technologies/frameview/frameview-2-0-user-guide-web-version.pdf | Not mentioned [F] | Privacy policy linked, not detailed [F] | Reflex latency markers (game-supported only) [S]; vendor-neutral GPU support |
| **NVIDIA App overlay** | In-game overlay: GPU/CPU use, 1% low, PC and system latency via Reflex Analyzer | Free with NVIDIA GPU [S] https://www.nvidia.com/en-us/software/nvidia-app.md | Vendor driver-integrated overlay (not user-mode-only independent) [U] | Not found | Not found | One-click inside tool users already have |
| **Special K** | Graphics fixes, frame limiting, perf analysis mods | GPL-3.0 [F] https://github.com/SpecialKO/SpecialK | DLL injection: proxy DLL (dxgi.dll etc.) or global CBT/Shell hookchain; late injection via CreateRemoteThread [F] | Developer says no known blacklist but advises avoiding it in games with non-disableable anti-cheat [S, ~2,800-day-old post] https://steamcommunity.com/groups/SpecialK_Mods/discussions/0/1843566500528115935/ . PCGamingWiki marks some games Blocked/Caution/Banned [S] https://www.pcgamingwiki.com/wiki/List_of_games_compatible_with_Special_K . BattlEye FAQ tells users to rename/remove dxgi.dll in game dir [F] ; Rockstar warns overlays like Special K may cause issues [S] | Not found | Extremely powerful; very high ban/crash risk by design |
| **LatencyMon** (7.40, Resplendence) | Kernel timer latency, DPC/ISR times, hard pagefaults; names offending drivers | Free Home edition and paid Professional [F] https://www.resplendence.com/latencymon | Driver use not stated on page [F]; Windows 11 support not stated [F] | Not found | Not found | Single pass/fail verdict plus the drivers responsible; audio-oriented framing, not games |
| **Process Lasso** (19.0.0.94, 2026-11-02 [S]) | ProBalance background-process throttling, affinity/priority rules | Free usable indefinitely with nag; Pro $39.95 (discounted $23.97) lifetime [S] https://bitsum.com/download-process-lasso/ | Official FAQ does not say whether it uses a driver [F]. Acts from outside processes, never touches process memory [F] https://bitsum.com/process-lasso-faq/ | EAC blocks direct access to game processes; workarounds via launcher rule and registry priority. Bitsum states no ban has ever been caused by Process Lasso (vendor claim) [F] | Sends no user/process data; contacts servers for updates and licence checks only [F] | ProBalance Insights log shows what it did and why |
| **Razer Cortex Game Booster** | Stops background processes/RAM, CPU "boost" modes, FPS counter, game clips; "Booster Prime" ML settings | Free per Razer [S] https://www.razer.com/cortex ; one listing says free tier shows upsell prompts [S] https://razer-cortex.updatestar.com/ | Not stated | Not found | Account linkage and telemetry reportedly documented by Razer; UpdateStar advises opting out [S, secondary]. Ad status conflicting across sources | Auto-activate on game launch and auto-restore |
| **Wise Game Booster and similar "boosters"** | Same category as Cortex | Not researched | Not researched | Not researched | Not researched | Gains reported small on high-end PCs [S] https://www.saashub.com/compare-razer-cortex-vs-wise-game-booster |
| **Windows Game Bar / Task Manager** | Game Bar performance widget with FPS (may need access prompt + reboot); Task Manager GPU Engine column per process | Built in, free | Microsoft component | Not found | Not found | Zero install. A Microsoft Tech Community thread dated 2026-07-28 reports the widget saying it cannot retrieve performance data [S] https://techcommunity.microsoft.com/discussions/windows11/after-opening-xbox-game-bar-the-performance-monitoring-panel-does-not-display-an/4541546 . Microsoft notes per-process GPU memory counters can read 0 and recommends WPR/WPA [F] https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/gpu-process-memory-counters-report-wrong-value |
| **AMD OCAT** | FPS overlay and capture for D3D11/D3D12/Vulkan | MIT tag on GitHub [F] https://github.com/GPUOpen-Tools/ocat | Repo contains a global hook, DLL injector and overlay components [F] | Not found | Not found | Effectively dormant: last release v1.6.3; "discontinued" only per AlternativeTo (third party); date sources conflict (Sep 2023 vs winget Aug 2025) [S] https://alternativeto.net/software/ocat-open-capture-and-analytics-tool/about |

### New diagnostic-first tools (closest to Jankscope)
- **Stuttometer** (GPLv3, 13 stars, 111 commits, no release entries seen): real-time ETW micro-stutter, frame pacing and audio-glitch diagnostic. Keeps a rolling trace, and on a spike saves the window and writes a JSON diagnosis with ranked probable causes (DPC/ISR, shader compile, VRAM paging, context switches). Needs admin; no kernel driver mentioned; states it does not inject or change scheduler priorities; has redact mode, PNG "stutter cards", CLI. [F] https://github.com/xdr0p/stuttometer ; context [S] https://forums.blurbusters.com/viewtopic.php?t=15732 . This is the most direct overlap with Jankscope's "explain why" promise. Maturity is low (star count and no releases) but the concept is the same.
- **SCSKiller** (GPL-3.0-or-later; first GitHub release reported 2026-09-29 [S]): precompiles shaders into the GPU driver cache; per-session frametimes; explicit anti-cheat guard (reads files only by default, warns about leftover files and bans). NVIDIA/AMD only, no Intel [F] https://github.com/BlueHeisenberg/SCSKiller . A fixer, not a diagnoser, but its anti-cheat consent flow is a good pattern.
- **PresentMon 2.6** now adds PSO compile tracking and a "perceived smoothness" overlay [F]; Intel is moving upstream into some of the "why" territory.
- "Stutter Fix 2026" repository describes itself as a "concept" [S]; ignore.

## The real gap Jankscope fills
1. **Explanation, not just measurement.** CapFrameX, FrameView, PresentMon, RTSS, Game Bar show numbers and charts. LatencyMon explains DPC/ISR but not for frame pacing. Only Stuttometer attempts ranked causes, and it is early-stage. [F sources above]
2. **One tool spanning three lag types.** Frame pacing, input stalls (Raw Input timing) and network lag in one evidence-graded report. No tool in this review covers all three; input-latency measurement elsewhere relies on NVIDIA Reflex markers (game-supported) or hardware like LDAT [S] https://images.nvidia.com/content/geforce/technologies/frameview/frameview-2-0-user-guide-web-version.pdf . This is an observation across the sources, not a source's own claim.
3. **Driver-free and injection-free.** CapFrameX bundles WinRing0 (flagged by Microsoft as vulnerable), HWiNFO and Afterburner are on the same Microsoft list, Afterburner/Vanguard clash is long-documented, Special K and OCAT inject [all F/S above]. A user-mode-only tool avoids Defender vulnerable-driver warnings and the Vanguard "incompatible driver" error path [F] https://support.riotgames.com/en-us/valorant/performance/van-incompatible-software .
4. **Evidence grading.** No competitor reviewed grades confidence of its own findings (not found in any source; absence of evidence, not proof).
5. **No telemetry, no upsell** versus Razer Cortex-style tools [S] and cloud-sharing features in CapFrameX [F].

## Table-stakes features users will expect
Derived from what the competitors above ship (sources per row in the table):
- Frametime graph, avg FPS, 1% and 0.1% lows, percentile table, frametime histogram (CapFrameX, PresentMon, Stuttometer, FrameView).
- CSV/JSON export of per-frame data (PresentMon, Stuttometer).
- Compare two runs / before-after (CapFrameX).
- Process/game picker and a capture hotkey (Stuttometer `Ctrl+F11`).
- Overlay or at minimum a live readout (PresentMon, FrameView, RTSS). Jankscope deliberately has none, so a clear "why no overlay" explanation and a post-session view are needed.
- GPU Busy / CPU-bound vs GPU-bound hint (PresentMon, Intel page).
- Latency number where the game exposes it (Reflex PC latency via FrameView / NVIDIA App; PresentMon click-to-photon since v2.2.0 per release notes [F]).
- Works on NVIDIA, AMD and Intel (FrameView and PresentMon are vendor-neutral).
- Clear admin/permission handling: ETW needs Performance Log Users membership or admin [F].
- Light CPU overhead (PresentMon 2.6 advertises up to 78% less CPU use [S]).
- Dark UI, shareable report/screenshot.

## Five UX ideas worth borrowing
1. **Auto-triggered "stutter cards".** Keep a rolling buffer and, on a spike, save the surrounding window as an exportable card with ranked probable causes (Stuttometer: PNG cards, clipboard copy [F]). Fits Jankscope's evidence-graded findings.
2. **Redact mode for sharing.** Strip process names, paths and usernames before export (Stuttometer [F]). Strengthens the no-telemetry privacy story when users post reports.
3. **Action log showing what the tool did and why.** Process Lasso's ProBalance Insights [F]. For Jankscope: a "what I measured, what I did not touch" panel that reinforces user-mode-only.
4. **Per-game anti-cheat consent flow.** SCSKiller's default-safe behavior (read-only for anti-cheat games, explicit per-launch confirmation, cleanup warning) [F]. Jankscope can show a "protected game detected: passive ETW only" badge.
5. **Perceived-smoothness view and named causes in plain language.** PresentMon's Game Experience preset reorients away from raw FPS [S]; LatencyMon names the specific offending driver/module [F]. Pair a smoothness score with "likely culprit: X" and confidence grade.

## Risks
**Anti-cheat**
- ETW-only is the most defensible design but not provably safe. PresentMon's own 2.5.0 notes say some titles block ETW trace collection and that they added changes to gather data in some of them [F] https://github.com/GameTechDev/PresentMon/releases . So expect games where capture fails (empty data). Jankscope must report "no data, game blocks tracing" instead of 0 or guessing. No source found names which anti-cheats do this.
- Elevated rights: ETW needs admin or Performance Log Users [F]. Vanguard, EAC and BattlEye run elevated components; none of the sources documents a ban from a passive ETW consumer. Do not claim "ban-proof"; claim "no injection, no driver, no memory access" (verifiable) and cite that BattlEye bans for cheats/hacks or components of them [F] https://www.battleye.com/support/faq/ .
- Vanguard On-Demand (24 Jun 2026) requires Windows 11 25H2+, Secure Boot, TPM 2.0, IOMMU, VBS and HVCI [F] https://en.wikipedia.org/wiki/Riot_Vanguard (Wikipedia; Tom's Hardware fetch was truncated). Strict-security users are a growing group; a user-mode tool aligns with that stack.
- BattlEye also says its driver conflicts with Windows 11 kernel-mode hardware-enforced stack protection [F]; unrelated to Jankscope but a source of user confusion that will be blamed on any new tool.
- Network-lag probing: keep it to the user's own traffic or standard probes; nothing here was researched for anti-cheat or ToS friction. [U]

**Legal / licence**
- PresentMon is MIT [F], so reuse of ETW logic is permitted with notice. CapFrameX licence text was not confirmed from the page; do not copy its code without checking. Stuttometer and Special K are GPL, so copying code would infect the licence. Jankscope is stated as open source; confirm its licence is compatible before borrowing anything. [U on compatibility]
- Do not use Intel/NVIDIA/Riot trademarks beyond nominative comparison. Avoid stating "anti-cheat safe" as fact. [U, general practice]
- Microsoft states the WinRing0 detection is valid and lists many tools [F]; saying "competitors ship a vulnerable driver" is supported, but keep wording factual and neutral.

**Reputation**
- "Game booster" category carries bad trust (small gains, upsells, telemetry) [S] https://www.saashub.com/compare-razer-cortex-vs-wise-game-booster . Position as a diagnostic, not an optimiser; never imply FPS gains.
- Overclaiming causes: evidence grading must be visible, and "unknown" must stay unknown. This is the main trust differentiator versus Stuttometer-style ranked "probable causes".
- Crowded free space: CapFrameX and PresentMon are free and mature; Stuttometer already claims the "why" angle, though at tiny adoption (13 stars at fetch time).
- Local MCP servers (CapFrameX has one bound to localhost without auth [F]) are a possible integration idea but also a security precedent: any local MCP in Jankscope should require auth and be off by default. [U recommendation]

## Source list (all URLs referenced)
- https://github.com/CXWorld/CapFrameX and /releases
- https://game.intel.com/us/intel-presentmon/
- https://github.com/GameTechDev/PresentMon and /releases
- https://www.techpowerup.com/353118/intel-presentmon-2-6-0-update-slashes-cpu-usage-adds-game-experience-overlay
- https://www.nvidia.com/en-us/geforce/technologies/frameview/
- https://images.nvidia.com/content/geforce/technologies/frameview/frameview-2-0-user-guide-web-version.pdf
- https://www.nvidia.com/en-us/software/nvidia-app.md
- https://support.microsoft.com/en-us/windows/security/threat-malware-protection/microsoft-defender-antivirus-alert-vulnerabledriver-winnt-winring0
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/gpu-process-memory-counters-report-wrong-value
- https://techcommunity.microsoft.com/discussions/windows11/after-opening-xbox-game-bar-the-performance-monitoring-panel-does-not-display-an/4541546
- https://support.riotgames.com/en-us/valorant/performance/van-incompatible-software
- https://en.wikipedia.org/wiki/Riot_Vanguard
- https://forums.guru3d.com/threads/rtcore64-sys-and-valorant-vanguard.431963/
- https://www.hwinfo.com/forum/threads/incompatibility-with-riot-vanguard-anti-cheat.6356
- https://www.hwinfo.com/version-history/ and https://www.techspot.com/downloads/1238-hwinfo.html
- https://en.wikipedia.org/wiki/RivaTuner
- https://www.battleye.com/support/faq/
- https://github.com/SpecialKO/SpecialK
- https://steamcommunity.com/groups/SpecialK_Mods/discussions/0/1843566500528115935/
- https://www.pcgamingwiki.com/wiki/List_of_games_compatible_with_Special_K
- https://www.resplendence.com/latencymon
- https://bitsum.com/process-lasso-faq/ and https://bitsum.com/download-process-lasso/
- https://www.razer.com/cortex and https://razer-cortex.updatestar.com/
- https://www.saashub.com/compare-razer-cortex-vs-wise-game-booster
- https://github.com/GPUOpen-Tools/ocat and https://alternativeto.net/software/ocat-open-capture-and-analytics-tool/about
- https://github.com/xdr0p/stuttometer and https://forums.blurbusters.com/viewtopic.php?t=15732
- https://github.com/BlueHeisenberg/SCSKiller
