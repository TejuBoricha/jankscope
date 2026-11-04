# Risk register

Spec §4.2. Reviewed at every stage gate. Severity: H / M / L (impact × likelihood, agent's estimate). Status: open · mitigating · accepted · closed.

| ID | Risk | Trigger / early sign | Mitigation | Owner | Sev | Status |
|---|---|---|---|---|---|---|
| R1 | An anti-cheat action or accusation is blamed on the tool | A user report of a ban/kick after installing | HC-1: documented user-mode interfaces only, no hooks/injection/overlays/drivers; context-aware suppression (ADR 0005); consent for game list; honest wording ("states what it does not do", never "anti-cheat approved"); static scan in CI | Agent | H | mitigating |
| R2 | Antivirus/SmartScreen false positives (Raw Input, hotkeys, process monitoring resemble keylogger behaviour) | Detections at release scan | HC-4 (timing only, key content never read), source open, signing path (ADR 0007), pre-release Defender false-positive submission | Agent / Owner | H | open (Stage 2+) |
| R3 | A wrong diagnosis erodes trust | "Wrong diagnosis" reports; fixture mismatches | Evidence grades, falsifiers, control windows, three fixtures per rule, wrong-diagnosis → regression fixture loop | Agent | H | open |
| R4 | An optimization harms a system | Failed rollback, user report | Allowlist, journal, rollback verification, Recommend-only default, no banned tweaks, unknown build ⇒ Recommend-only | Agent | H | open (Stage 4) |
| R5 | Upstream change in PresentMon or Windows | Pulled release (v2.5.0 was removed), changed CSV columns, new Windows build | Pin exact version and hash, tolerant parsers, capability probe, compatibility matrix | Agent | M | mitigating |
| R6 | Single-maintainer dependency | Owner unavailable | Docs, tests, STATUS ledger, contribution path, MIT license | Owner | M | accepted |
| R7 | Low donation income | Sponsor stats | No backend, near-zero running cost; optional Store edition | Owner | M | accepted |
| R8 | Scope creep | Stage slips | Release tiers (MVP/v1.0/P1/P2), cut-from-the-bottom rule | Agent | M | mitigating |
| R9 | Legal or trademark problem (name, claims, disclaimers) | Search hit, complaint | Name checked only informally (not clearance); formal trademark search and optional legal consult before release; no performance guarantees | Owner | M | open |
| R10 | Probes report a plausible-but-wrong state (the MDM false positive proved this class of bug is real) | Bench run shows a surprising row | "Unknown over guess"; documented APIs over registry heuristics; run the probe on a real machine at every stage; regression test per bug | Agent | M | mitigating |
| R11 | Dated facts in the spec are wrong | `docs/FACTS.md` rows marked open/contradicted | Verify before a decision depends on a fact; log source and date | Agent | M | mitigating |
| R12 | The ETW session limit or an orphaned session breaks other tools | Headroom near 0; own-prefix orphans | Count sessions, name ours with a unique prefix, stop only our own, verified cleanup test | Agent | M | mitigating |
