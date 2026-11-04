# Prompt review — v3 → v4 (2026-10-16)

Review of `MASTER_PROMPT_v3.md`. `MASTER_PROMPT_v4.md` is the working spec; v3 is kept unchanged for comparison. Every v4 change is marked **[v4]** in the file.

## Defects found in v3 and how v4 resolves them

| # | Defect in v3 | v4 resolution |
|---|---|---|
| 1 | HC-1 suppressed the elevated helper in protected games, and frame capture option (a) lived in that helper, so frame pacing was unavailable for the most common case. | Unelevated user-mode ETW (PresentMon console) is the in-game path; explicit coverage matrix in §6.6; "unavailable" states name the user action (HC-1, §6.6, #27). |
| 2 | HC-1 triggered on "any anti-cheat service running"; Vanguard loads at boot, so Tier C would be permanently off on the reference PC. | Context-aware states: resident-only suppresses nothing; game session or protected process suppresses the app's *own* elevated collectors only; the app never stops or modifies other software (HC-1, §2.2 #8). |
| 3 | Input differential diagnosis needed a keyboard/touchpad control with ≥ 5 events/s, so most verdicts would be "Inconclusive"; frame pacing/timer probe controls were ambiguous. | Two control classes (input, system) with explicit verdict caps; motion-continuity rule so an idle mouse is not a "stall" (§8, #29). |
| 4 | Engine↔UI channel was only "access-controlled" while the helper was hardened; the Engine relays the approval token. | Channel hardened like the helper's; PID + creation time; helper's only client is the Engine; token bound to action/params/nonce/expiry; trust boundary stated (§6.1, #28). |
| 5 | HC-7 forbade download-and-run, but §6.6(d) allowed a runtime PresentMon download. | Bundle a pinned build at build time; runtime download rejected (HC-7, §6.6). |
| 6 | Ad slot allowed on Overview (a diagnosis screen) while HC-13 banned ads on diagnosis screens; ad ADR "approval" had no owner. | Moot: the owner chose a donation link; ads removed entirely (§4.1, HC-13, #21–#25). |
| 7 | HC-12 lint would flag the NVML/ADLX/IGCL adapters that §3.1 requires. | Allowlisted vendor-API adapter assemblies (§3.4, HC-12, #17). |
| 8 | "Not elevated" was treated as "standard user"; the bench is an admin account already in *Performance Log Users*. | Documented as unrepresentative; standard-user and not-in-group paths simulated until the owner supplies an account (§2.1, §2.3, §3.2, #15). |
| 9 | Engine lifecycle and unelevated game detection unspecified. | On-demand start, opt-in autostart (default OFF); process-list polling at ≤ 1 Hz (§6.1, §13). |
| 10 | No release cut line; ads/cloud peers of the core. | Release tiers MVP / v1.0 / P1 / P2 (§2.2 #7, §17). |
| 11 | Writes through informally documented interfaces (GPU preference, power-mode overlay, Game Bar) were not distinguished from documented ones. | Documentation class per interface; informally documented writes are Recommend-only until validated on the build range (HC-10, §12, #30). |
| 12 | Dated external facts (versions, licenses, terms) stated as settled. | Fact register `docs/FACTS.md` and a verify-before-use rule (§1 #8, #31). |
| 13 | `PROMPT_REVIEW.md` referenced but absent; no product name; "booster" naming contradicted the honesty positioning. | This file; the product is named **Jankscope** (§2.2 #10, resolved in v4.1 below). |

## Business-stakeholder additions (§4.2)
Stakeholder needs, positioning and a sourced competitive review; personas and jobs-to-be-done; success measures that need no telemetry; published trust pledges; distribution and release management; a support and feedback loop (support bundle, "report a wrong diagnosis" → regression fixture); contributor and security-disclosure policy; legal/licensing (MIT, dependency license policy enforced in CI); risk register (incl. antivirus false positives from Raw Input + hotkeys); roadmap; a release go/no-go gate with a budget statement. Acceptance items #32–#35.

## Decisions recorded on the owner's delegation to best practice
Release tiers; donation link instead of ads (explicit choice); MIT open source with a local-only repository; context-aware anti-cheat handling; settled defaults in §2.3 (layout, signing path, autostart, language, OS support, installer, data location, crash handling).

## Owner-delegated items, resolved later the same day (v4.1)
The owner delegated every remaining open item to the agent. Resolved by research, or by a stated procedure where the agent cannot act (details in v4 §2.3 and §4.1; sources go to `docs/FACTS.md`):

| Item | Outcome |
|---|---|
| Earning | GitHub Sponsors (primary; India supported, personal sponsorships fee-free) plus an optional paid Microsoft Store convenience edition (precedent: the Files app). Ko-fi/UPI optional after verification. Open Collective and Patreon rejected. |
| Name | **Jankscope** (backups HitchLens, StutterLens). Unclaimed on web search, five TLDs, GitHub and npm — not trademark clearance. |
| Signing | Azure Artifact Signing is not available in India. Order: SignPath Foundation (free OSS) → Store MSIX (Microsoft signs) → purchased certificate as the owner's call. |
| Standard-user account | The agent cannot create accounts. Interim: restricted-token runs labeled as simulation; optional real account via `OWNER_CHECKLIST.md`. |
| First real game | Aim Lab (store page lists no anti-cheat; file-name scan clean), fallback PC Building Simulator; protected titles excluded. Re-verified at Stage 1b. |
| Beta testers / legal / publishing | Agent prepares drafts, templates and release packages; it does not recruit, post, give legal advice, or publish without a per-action yes. |

## Still the owner's by nature (none blocks development)
Account creation and identity checks (GitHub, Sponsors, optional Partner Center), formal trademark search, bank/tax handling, posting recruitment messages, and each public release action — see `OWNER_CHECKLIST.md`.

## Not verified by this review
Dated factual claims inherited from v3 (PresentMon versions and assets, .NET end-of-support dates, WinUI 3 limitations, ETW session limits, event IDs, ad-network and speed-test terms) were not re-checked. v4 makes the agent verify and record them in `docs/FACTS.md` before relying on them.
