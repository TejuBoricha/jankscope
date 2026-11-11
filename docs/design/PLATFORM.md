# Platform foundation: designing for the product we intend to become

Status: proposal, for review. Written after the owner's directive to design for a PC hardware
intelligence, monitoring, diagnostics and optimization platform, not only the current app.

This document sets the architecture, design system, and rules that every future module must
follow. It does not add features. It also states where the vision conflicts with the project's
hard constraints. Those conflicts need an owner decision before any design work depends on them.

---

## 1. The decision that comes first

The vision has two kinds of capability, and they need different foundations.

| Kind | Examples | What it needs | Status under current constraints |
|---|---|---|---|
| **Observe** | frame times, 1% lows, CPU/GPU load, clocks and power states *where documented user-mode interfaces expose them*, memory pressure, thermal throttling evidence, historical trends, anomaly detection, diagnostics | Documented user-mode reads (ETW, PDH counters, documented OS and vendor management APIs) | Allowed in principle, one source at a time, each verified before it is shown |
| **Change** | overclocking, undervolting, power limits, fan curves, sensor reads that need a driver, automated tuning | Writes to hardware, usually through a kernel-mode driver or a vendor driver, or an elevated tool | **Conflicts with HC-1 and HC-2** (`docs/RISKS.md` R1, `docs/THIRD_PARTY.md`): no drivers, no injection, no overlays, documented user-mode interfaces only |

Reads of hardware sensors are also limited. Many temperature, voltage and fan readings are not exposed through
documented user-mode Windows APIs. We have not verified which sensors are reachable this way. That needs a
feasibility spike per sensor family before any screen promises it.

**Owner decision needed (blocks sections 5 and 6 of this document):**

- **Option A (current constraints):** Jankscope is a read-only observer and advisor. Optimization is shown as
  guidance: what to change, the expected trade-off, and how to check it afterwards. The user makes the change
  in the tool they already trust. Jankscope can measure the before and after. It never writes to hardware.
- **Option B (amend constraints):** Allow a narrow, signed, optional write component for specific actions
  (for example fan curves through a vendor-approved path). This is a trust and anti-cheat decision, not a UI
  decision. It needs an ADR and a change to `docs/RISKS.md` before any design work depends on it.

Everything below works under Option A. Option B only adds rows to the action catalog; it does not change the
layout, the components or the navigation.

---

## 2. Principles

1. **One product, many modules.** A new module adds content to an existing space. It does not add a new app
   shell, a new visual language or a new navigation model.
2. **Facts before explanations.** Every number is labelled as measured, derived, or a hypothesis (`docs/RULES.md`).
   Recommendations are evidence-linked, never unexplained.
3. **Unknown is not zero.** A missing sensor shows "not available on this PC, and why", never a 0.
4. **Show the top answer first, then the evidence, then the raw numbers.** Three disclosure levels (section 7).
5. **Nothing changes silently.** Any action that changes a system state requires a separate, explicit consent.
6. **Calm by default, alive when it matters.** Real-time data moves; the chrome does not compete with it.
7. **Consistency is in tokens and components, not in layouts.** Monitoring, decision, investigation, configuration
   and history each get their own layout pattern, built from the same parts.

---

## 3. Information architecture

Six top-level spaces. The count stays fixed so the navigation never grows with the product. New modules go
inside a space.

| Space | Question it answers | Today | Future modules that fit |
|---|---|---|---|
| **Watch** | What is happening right now? | Live capture, frame timing, mark | Live telemetry (CPU, GPU, memory), thermal and power state, frame-rate summary |
| **Replay** | What happened, and when? | Incident workbench, candidates, causes | Event timelines across all modules, comparison of two replays |
| **History** | How has this changed over time? | Not yet (the saved replays only) | Trends, session reports, before-and-after comparisons, degradation over weeks |
| **Tools** | What can I check or test? | Capability check, input lab | Stability tests, hardware inventory, driver and firmware checks, component diagnostics |
| **Optimize** | What could be better, and what would it cost? | Not yet (recommend-only, Stage 4) | Recommendations with trade-offs, profiles, guided changes (Option A) or applied changes (Option B) |
| **Settings and Trust** | How do I configure it, and what does it do with my data? | Settings, Trust | Module settings, alerts and thresholds, themes, export rules |

Today's "Share" (the export preview) is not a space. It is an action on a replay or a report, and it keeps the
"what leaves your PC" review as its single pattern.

Navigation rules:

- A space is shown only if at least one module in it is available on this PC.
- Within a space, sections are the module boundaries. A module can be hidden if unsupported, with a visible reason.
- Global elements (the mark hotkeys, the capture status, the "no hooks, offline" line) live in the frame, not in a space.

---

## 4. Module contract

A module is a unit of code and content with a fixed shape. The shape is what keeps modules consistent.

Every module declares:

1. **Sources.** Each data source it reads, with its provenance (measured, derived, fixture, simulated) and its
   requirement (documented interface, admin rights, a specific vendor). A source that cannot be verified on this
   PC is reported as unavailable with its reason.
2. **Panels.** Widgets from the shared catalog (section 6), each with a default size and minimum size on the grid.
3. **Explanations.** Plain-language text for each reading, at the Glance and Explain levels, plus the technical
   definition at the Detail level.
4. **Findings.** Zero or more evidence-linked findings (section 8), each with a grade and a falsification test.
5. **Actions.** Zero or more actions, each with a classification (reversibility, scope, blast radius), a
   precondition check, and a consent step (section 8). Under Option A, an action is a guide, not a change.
6. **Settings.** Thresholds, visibility and notification rules, shown in the shared Settings space.
7. **Compatibility.** Which systems it was tested on, and which it reports as unsupported.

A module cannot draw its own colours, fonts, spacing or motion. It uses the tokens and components of section 5.
This is what makes a new module feel like part of the product.

---

## 5. Design system

### 5.1 Foundation (tokens)

- **Colour:** a surface scale, text scale, one accent (the instrument cyan), and a small set of status colours
  (ok, warning, danger, simulated, neutral). Each token exists for dark and light themes. Status colours are never
  the only way to convey meaning; they always appear with a word or a shape.
- **Type:** one sans family for text, one monospace family for numbers and identifiers. A fixed scale (about 6 steps).
  Numbers use tabular figures so live values do not jitter.
- **Spacing and grid:** an 8-point base, and a 12-column dashboard grid where widgets snap to units.
- **Motion:** short, purposeful transitions (state changes, expansion). Live data updates in place without animation
  on every tick. A reduced-motion setting disables everything except state feedback.
- **Elevation and borders:** hairline borders and surface steps, not heavy shadows.

### 5.2 Components (shared, used by every module)

| Component | Purpose |
|---|---|
| **Reading** | a labelled value with unit, provenance badge, freshness, and an "explain" affordance |
| **Live indicator** | shows that a source is live, stale, paused, or unavailable |
| **Chart** | time series, with the rules in 5.3 |
| **Status summary** | one sentence with a state (for example "Limited by GPU for the last 30 s") |
| **Finding card** | claim, evidence, grade, what would falsify it, and what to check next |
| **Action panel** | the action, its trade-offs, its risk class, its precondition results, and its consent control |
| **Comparison** | two runs side by side, with the difference stated in plain words |
| **Alert** | severity, source, threshold, and the time it started; in-app only |
| **Disclosure** | the three-level expander (section 7) |
| **Panel frame** | title, one-line caption, actions, and the state of the source |

Components have one set of states: loading, live, stale, paused, unavailable (with reason), error (with what to do).

### 5.3 Chart and visualization rules

- Time always runs left to right on the same axis definition across modules.
- Every chart states its window, its sample rate, and its source.
- Thresholds are drawn as labelled lines, never as colours only.
- Anomalies are marked on the time axis and linked to their finding.
- Comparisons use the same axes and say what changed in words.
- Charts never animate a value that the user is trying to read.

### 5.4 Experience patterns

Each pattern is a layout built from the components above. The pattern is chosen by the job the user is doing.

| Pattern | Job | Priority | Used in |
|---|---|---|---|
| **Monitor** | watch values change | clarity, real-time visibility, one glance | Watch |
| **Investigate** | find out why something happened | evidence, findings, timelines, what to check next | Replay, Tools |
| **Decide** | choose a change | trade-offs, risk, before and after, consent, rollback | Optimize |
| **Configure** | set precise values | progressive disclosure, validation, safe defaults, reset | Settings, advanced panels |
| **Trend** | understand change over time | time axis, comparison, summaries of periods | History |

The same components appear in every pattern. The layout and the emphasis differ.

---

## 6. Customization without losing consistency

- **Dashboards:** a dashboard is a set of panels on the grid. Users can add, remove, resize and reorder panels from
  the shared catalog. Presets ship for common goals (gaming session, thermal check, quiet desktop).
- **Thresholds:** every metric with a warning or danger level has an editable threshold, with the default and the
  reason shown next to it.
- **Themes:** a theme is a set of token values only. Users can choose a theme and density. They cannot change
  component structure, so screens stay recognisable.
- **Modes:** Beginner and Advanced are disclosure levels, not separate apps. Both modes use the same screens. Beginner
  mode shows the Glance and Explain levels by default. Advanced mode opens the Detail level by default.
- **Export:** a dashboard or report exports through the same "what leaves your PC" review as today.

Limits: customization cannot hide a safety-relevant status, remove a provenance badge, or remove the explanation of
an action's risk.

---

## 7. Progressive disclosure

Every reading and finding has three levels:

1. **Glance:** one value and one sentence. "GPU is the limit for the last 30 seconds."
2. **Explain:** what the value means, in plain language, and why it matters for the user's goal.
3. **Detail:** the technical definition, source, sample rate, raw values, formula, and the export of the raw data.

Rules:

- The default level depends on the mode (section 6), never on the module.
- A user can open any level from any reading in one step.
- Technical terms are defined at the Explain level the first time they appear on a screen.
- No screen shows more than one primary number per panel at the Glance level.

---

## 8. Intelligence, recommendations and actions

### 8.1 Findings

Findings extend the existing rules engine (`docs/RULES.md`). Each finding has:

- The claim, in one sentence.
- The evidence: each fact with its timestamp and source, and each inference with its calculation.
- The grade (A to F) and why it has that grade.
- What would falsify it, with the exact measurement that would show it.
- What to check next, as a concrete step the user can take.

Findings never use the words "AI recommends" as their justification. The justification is the evidence.

### 8.2 Recommendations and trade-offs

A recommendation states:

- The expected benefit, as a measurement the user can check (for example, "1% low frame time should fall").
- The expected cost (for example, higher idle power, or less headroom at high temperature).
- The condition under which it would not help.
- How the user verifies the effect: a before-and-after measurement with the same workload.

### 8.3 Actions

Each action is classified by reversibility, scope, and blast radius (`docs/OPTIMIZATIONS.md`). Every action shows:

- Its precondition results, each as pass, fail, or unknown with the reason.
- Its risk statement in plain words.
- A consent step that names the action. Consent is never shared across actions.
- A rollback path, and for changes, the recorded previous value.
- A verification step after the change.

Under Option A, an action produces a guide and a verification plan. Under Option B, the same panel also applies the
change, subject to the existing journal and rollback rules in `docs/ROLLBACK.md`.

### 8.4 Not in scope without a constraint decision

- Automatic tuning of any kind.
- Stability tests that load hardware for long periods without the user starting them and watching the result.
- Fan control, voltage control or power-limit changes, which need writes to hardware (section 1).

---

## 9. Alerts and notifications

- **In-app first.** Alerts appear in the frame and in the Watch space. No overlays (HC-1).
- **Tray icon state** shows the highest current severity, with a word in the tooltip, not colour alone.
- **Rules are user-defined:** metric, threshold, duration, and quiet hours.
- **Quiet by default:** a new module starts with no alerts enabled.
- **Rate limits:** repeated alerts for the same cause merge into one, with a count.
- **History:** every alert is kept in the Replay space with its context.

---

## 10. Feature integration checklist

Every new feature answers these before design work starts. The answers are recorded in the module's entry in the
catalog.

1. Where does it belong in the information architecture (section 3)?
2. Does it need a new module, or does it extend an existing one?
3. Which existing components and patterns does it reuse?
4. Does it introduce a new interaction pattern? If yes, why can an existing pattern not serve?
5. How do beginners discover and understand it (Glance and Explain levels)?
6. How do advanced users reach its technical depth (Detail level, export)?
7. Does it add complexity to the default view? If yes, where is it hidden?
8. Can it grow with related capabilities without changing the layout?
9. Does it keep performance (no more than the frame budget for live panels) and accessibility (keyboard, contrast, reduced motion)?
10. Does it keep the established design identity (section 5 tokens, the facts-first labels, the provenance badges)?
11. Which constraint does it touch (HC-1 to HC-13), and is the answer "none", or a named decision?

---

## 11. Competitors: what to learn, what to avoid

This section is from general knowledge of these products, not from a fresh review of their current versions. It
needs a verified pass before it informs any decision.

| Product family | What it does well | Where it falls short for our goals |
|---|---|---|
| Overlay-based monitors and tuners | Familiar, fast to read in play | Overlays and driver dependencies conflict with HC-1 and HC-2; tuning is a list of controls with little guidance |
| Deep sensor tools | Very broad sensor coverage | Dense, one-screen-of-everything; sensor availability varies by vendor and driver; hard for beginners |
| Vendor tuning suites | Integrated with the hardware | Locked to one vendor; often driver-based; the experience changes from one product generation to the next |
| Fan and cooling controllers | Clear curve editing | Driver and hardware dependence; little context on whether the change helped |
| Vendor app hubs | Convenient for common settings | Fragmented across apps; settings are hard to find; little history |

What to take: clear status, one-screen summaries, a single place for history, plain-language explanations.
What not to copy: overlays, driver-based writes without consent, dense single screens, and vendor lock-in.

---

## 12. How this shapes the phases

- **Phase 2 (tokens and components)** implements section 5 as the only source of styles. The shared components of
  5.2 are built there, so Watch, Replay and later modules use them from the start.
- **Phase 3 (Watch) and Phase 4 (Replay)** become the Monitor and Investigate patterns (5.4).
- **Phase 5** adds the Settings and Trust surfaces for the module and alert rules (sections 6 and 9).
- **History and Optimize** are not in the current phases. They get their own phase after the owner decides the
  constraint question in section 1.

---

## 13. Decisions needed from the owner

1. **Constraint question (section 1):** Option A (observer and advisor) or Option B (narrow write path, requires an
   ADR and a change to the risk register).
2. **Sensor feasibility:** approve a short spike to check which temperature, voltage, fan and power readings are
   reachable through documented user-mode interfaces on a few reference machines.
3. **Competitor pass:** approve a verified review of current versions of the products in section 11, or accept the
   general-knowledge summary for now.
4. **Mode names:** confirm "Beginner" and "Advanced" as the disclosure modes, or give other names.
