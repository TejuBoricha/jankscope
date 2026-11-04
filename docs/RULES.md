# Diagnostic Rules Architecture

Jankscope transforms raw performance metrics and system posture signals into actionable root-cause hypotheses. This document defines the formal rules engine architecture, evidence evaluation criteria, and confidence scoring model (spec §10).

---

## 1. Epistemological Separation: Facts, Inferences, and Hypotheses

To prevent anecdotal guesswork, Jankscope enforces a strict three-tier classification for all diagnostic statements:

1. **Facts (Measured):** Direct observations from hardware counters, ETW events, or operating system APIs. Examples:
   - "Frame time was 62.4 ms at timestamp 14:02:11."
   - "DPC execution time remained below 0.1 ms during the interval."
2. **Inferences (Logical Deductions):** Direct mathematical or logical derivations from facts without speculation. Examples:
   - "99th percentile frame time is 38.2 ms."
   - "Frame delay exceeded baseline rolling median by 4.2x."
3. **Hypotheses (Explanations):** Plausible causal explanations for why an observed event occurred. Every hypothesis must state:
   - The proposed root cause.
   - An assigned confidence grade.
   - Specific supporting evidence.
   - **What would falsify it:** The exact empirical data that would disprove the hypothesis.

---

## 2. Confidence Grading Scale

| Grade | Confidence Level | Definition & Criteria | Example |
|---|---|---|---|
| **A** | **Proven Direct Cause** | Direct causal evidence linking the specific event to the observed hitch with zero ambiguity. | GPU driver device-lost reset event occurring exactly at the frame freeze timestamp. |
| **B** | **Strong Correlation** | Strong statistical alignment between hitch timing and an anomalous system event. | Storage disk active time spiked to 100% with a 300 ms queue latency immediately preceding the stutter. |
| **C** | **Plausible Factor** | Known risk factor or resident subsystem present, but causal timing link is circumstantial. | A resident kernel anti-cheat driver is running on a system reporting intermittent present delay. |
| **D** | **Weak / Speculative** | General system condition that can contribute to instability, but lacks temporal correlation. | System power plan set to "Balanced" on desktop AC power. |
| **F** | **Falsified** | An initial hypothesis definitively disproven by counter-evidence. | CPU bottleneck hypothesis disproven when all CPU core usages remained below 25% during the hitch. |

---

## 3. Core Diagnostic Rule Definitions

### Rule R-01: Frame Pacing Hitch (Presentation Delay)
- **Trigger:** Swap-chain frame presentation delta exceeds rolling 10 s median by $\ge 3.0\times$ and absolute excess $\ge 12.0\text{ ms}$.
- **Evidence:** Frame timestamps recorded via ETW `DxgKrnl` provider.
- **Grade:** B.
- **Falsification Condition:** GPU engine render completion logs showing frame ready before target present interval.

### Rule R-02: Resident Anti-Cheat Coexistence
- **Trigger:** Resident kernel driver detected (e.g., `vgk.sys`) with active game session.
- **Evidence:** Service status query indicating driver service in running state.
- **Grade:** C (capped at Grade C; cannot claim anti-cheat caused hitch without DPC proof).
- **Falsification Condition:** DPC/ISR latency capture proving zero interrupted present calls during hitches.

### Rule R-03: Storage Asset Stream Hitch (Stage 3)
- **Trigger:** Disk response time exceeds 50 ms concurrently with an isolated presentation drop.
- **Evidence:** PDH disk physical read/write response time counters.
- **Grade:** B.
- **Falsification Condition:** Disk queue length is 0 and read bytes are 0 during the entire frame interval.

### Rule R-04: Motion-Continuity Input Stall (Stage 2)
- **Trigger:** Mouse movement delta abruptly drops to 0 while frame presentation continues smoothly at target refresh rate.
- **Evidence:** Raw Input message arrival timestamps vs. PresentMon swapchain intervals.
- **Grade:** A.
- **Falsification Condition:** User physically stopped moving the pointing device (idle state verified by control-validity baseline).
