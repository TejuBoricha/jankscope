# Business notes — Jankscope

Spec §4.2. Status: Stage 0 draft. Everything marked *(unverified)* comes from web search results or vendor pages read on 2026-10-16, not from hands-on testing of the competing tools.

## Positioning
Jankscope explains **why** a game stutters or an input device freezes — it shows evidence and its uncertainty — and changes a setting only when it can prove the benefit and undo the change. It is not a "booster": no promised FPS gains, no cleaners, no superlatives in UI, docs or store text.

## Competitive review
What each tool does and does not do, as described by its own pages or reviewers.

| Tool | What it does | Cost / license | Gap relative to Jankscope's goal | Source (read 2026-10-16) |
|---|---|---|---|---|
| PresentMon (Intel) | Frame-time capture engine and CSV logs; v2.6.0 adds a UI, telemetry providers and overlay presets | Free, MIT | Produces data, not diagnoses | github.com/GameTechDev/PresentMon (README, releases) |
| CapFrameX | Frame-time capture and analysis built on PresentMon: graphs, comparisons, 1%/0.1% lows, overlay via RTSS | Free, open source (exact license *unverified*) | Excellent analysis for benchmarking; does not attribute a stutter to a cause or grade evidence *(unverified, from descriptions)* | heise.de, computerbase.de, release pages via search |
| NVIDIA FrameView | PresentMon data plus NVIDIA power/efficiency data | Free, freeware | Vendor-focused measurement, no cause analysis *(unverified)* | guru3d/hothardware via search |
| MSI Afterburner | Monitoring and overclocking; frame-time graphs come from companion tools | Free, freeware | Different job (tuning) | pcwelt/windowsreport via search |
| LatencyMon (Resplendence) | Measures kernel timer latency, DPC/ISR times and hard page faults to judge real-time audio suitability | Free edition plus paid Professional edition *(unverified)* | Strong on DPC/ISR, not game-frame-centric, no input or network view; its own DPCs skew totals (vendor says so) | resplendence.com via search |
| Process Lasso (Bitsum) | Dynamic process priority/affinity management ("ProBalance") | Freemium | Applies changes automatically; Jankscope bans blanket priority/affinity loops | wikipedia/bitsum via search |
| Razer Cortex "Game Booster" | Claims to raise FPS by managing background apps | Free | Marketing claims; independent reviews and user reports are critical *(old/anecdotal)* | razer.com, softpedia, Razer Insider via search |
| HWiNFO | Deep hardware sensors; commercial use and automation need a Pro license | Free personal / Pro licence | Sensors, not diagnosis | hwinfo.com via search |

**The gap Jankscope targets (to be proven, not assumed):** a tool that ties frame-time anomalies, input-delivery gaps and network behaviour to ranked, evidence-graded hypotheses with falsifiers — and that proves its own recommendations help with a before/after statistic. None of the tools above, as described, does all of this together.

## Personas and jobs-to-be-done
1. **Casual gamer** — "Why does my game hitch, and what should I try?"
2. **Peripheral sufferer** — "Is my wireless mouse or receiver dropping?"
3. **Competitive player** — "Is it my PC, my Wi-Fi or the server?"
4. **Enthusiast / streamer** — "Give me raw, exportable frame-time evidence."

Each persona gets a scripted usability scenario in Stage 5 (`docs/FAQ.md` mirrors their questions).

## Success measures (no telemetry)
Initial targets, to be revised with beta evidence:
- Install to first useful report ≤ 10 minutes for persona 1 (moderated sessions, ≥ 5 participants supplied by the owner).
- Stutter detection within ±1 frame on the harness (Stage 1a exit).
- 0 unrecovered failed optimizations; 100 % rollback success in the failure-injection suite.
- Crash-free sessions ≥ 99.5 % in soak and beta logs (read locally).
- 0 antivirus/SmartScreen detections at release (Defender plus an owner-run multi-engine scan).
- "Wrong diagnosis" reports tracked per rule id; adoption read only from voluntary public signals (downloads, issues, sponsor statistics), never from the app.

## Trust pledges (to be published in the README and `PRIVACY.md`; each backed by a test)
No telemetry · no network use except user-approved probes and user-clicked links · no hooks, injection, overlays or drivers · every change logged, approved and reversible · open source · SHA-256 checksums and an SBOM with each release.

## Budget and assumptions
| Item | Expected cost | Who pays | Status |
|---|---|---|---|
| .NET SDK, tests, CI-style build | 0 | — | done |
| Code signing | 0 if SignPath accepts the project; otherwise a certificate (Certum open-source kit reported around €69 plus a token — *unverified*) | Owner decides | Stage 5 |
| Domain | optional, yearly fee | Owner | optional; GitHub Pages works without |
| Cloud compatibility tests | free tier only; verify current terms | Owner-created accounts | P1 |
| Legal consult, accountant | optional but recommended before release | Owner | pre-release |
| Revenue | unknown; GitHub Sponsors and an optional Store edition | — | **assumption**: small and irregular; no numbers promised |

The agent spends nothing without the owner's approval.
