# Design audit: the whole application, as it is on `main`

Scope: every window, page, dialog, tray menu item, empty state and notice in
`src/Jankscope.App` (about 6,000 lines of XAML and code-behind). Written before any
visual change, so the redesign has a list to be measured against. Each finding names
the place it was found, so it can be checked.

## 1. Screens and entry points

| Surface | Where | What a user does there |
|---|---|---|
| Welcome | `WelcomeWindow` (first run, and from Settings) | Reads what the program reads and never does |
| Live capture | Deck 01, `PageCapture` | Picks a program, starts capture, watches live stats, marks a stutter |
| Forensic workbench | Deck 02, `PageInvestigator` | Chooses a mark, scrubs the timeline, picks the spike that caused it, writes a note |
| Capability radar | Deck 03, `PageRadar` | Reads the hardware and OS checks |
| Input polling lab | Deck 04, `PageLab` | Watches input polling (mouse/keyboard timing) |
| Studio configuration | Deck 05, `PageSettings` | Hotkeys, pre/post-roll lengths, sound, theme |
| Spec and privacy | Deck 06, `PageAbout` | Reads the constraints, exports a compatibility report, links to sponsor/store |
| Export preview | `ExportWindow` | Reviews the exact text, removes personal information, saves |
| Tray | `TrayIcon` | Mark stutter, mark input stall, open, quit |
| Notices | bottom bar of the main window | One-line messages that fade |

Forty-plus distinct controls and labels were read. Nothing in the list above was
skipped; the three internal analysis views (`TimelineView`, `SpikeAnatomyView`,
`FrameDistributionView`) and `InputPollingView` were read for their labels.

## 2. The product does not explain itself

1. **The first screen is a tool, not an explanation.** A user who opens the app is
   shown "CH1 // FRAME RATE (5S)", "CH5 // RING INTEGRITY" and "BUDGET HAIRLINES".
   Nothing says what the product is for, in the first sentence the user reads.
   The one-line tagline ("Explains why a game stutters") is in a 10 px sidebar
   footer, not on the page the user lands on.
2. **The task is in a sticky banner of jargon.** The top bar reads "[STEP 1] Target
   3D App or Simulator [STEP 2] Press Ctrl+Alt+F9 on Hitch [STEP 3] Inspect 31-Frame
   Waterfall & Export". "Hitch", "waterfall" and "31-frame" are not words a player uses.
3. **Navigation is named after internal tools.** Decks are "Live Capture Scope",
   "Forensic Workbench", "Capability Radar", "Input Polling Lab", "Studio
   Configuration", "Spec & Privacy Proof". The user's question is "why did this
   stutter?", and nothing in the rail answers it. "Radar" and "Lab" describe what the
   developer built, not what the user will do.
4. **The mental model is split across pages.** A mark is made on Capture, examined on
   Forensic Workbench, and exported from the workbench's button. Live stats are on
   Capture, and the same numbers come back as "WINDOW AVG FPS" on the workbench, with
   different names (`FPS` vs `frames/s` vs `FRAME RATE`). A user has to learn one
   object (the mark) in three places.
5. **Three names for the product.** The window title is "Jankscope Studio - Frame
   Pacing & Stutter Forensics", the header is "JANK//SCOPE", and the docs, the
   welcome text and the export are "Jankscope".
6. **Three names for one thing.** The same event is called "input stall" (Capture
   buttons, Investigator, tray and the export), "Input Freeze" (Settings: "Mark Input
   Freeze Chord") and "freeze" in the Settings tone label ("Input-Freeze Tone"). One
   concept, one name is the minimum.
7. **Internal codes shown to users.** "HC-1..13", "HC-4 REGISTERHOTKEY" and
   "COMPILE-TIME HARD CONSTRAINTS" appear on the Settings and About pages and in the
   sidebar footer ("HC-1..13 // ZERO HOOKS // OFFLINE"). These are the owner's audit
   references, not user information.
8. **A contradiction about money.** The About page says "zero feature gates", and the
   header shows a "PRO" badge. A user will read "PRO" as a paid tier. The product's own
   docs describe no such tier (the optional Store edition is a distribution choice,
   not a feature gate).

## 3. Visual language

9. **Generic dashboard vocabulary.** KPI cards with "-- FPS" placeholders, five
   channel cards, a "CH1 ... CH5" grid, panels with uppercase overline labels. Every
   page uses the same card and overline pattern, so nothing stands out as the place
   to look.
10. **Too many containers.** Nested cards inside cards (for example the capture
    status card holds the chart, which sits on a panel which sits in a hero band).
    Each border adds visual weight without adding meaning.
11. **Decoration competes with data.** Phosphor-cyan glow on the accent, a
    "digital phosphor" motif, a "Poincaré phase-space radar" and "radar bullseye
    reticle" are all decoration on top of the numbers the user came for.
12. **Small type for important information.** Much of the explanatory text is
    9.5 to 11.5 px Consolas. The explanations that matter most (what a mark means, what
    uncertainty is) are the smallest text on the page.
13. **Colour meaning is overloaded.** Red is "danger", "a spike", and a candidate
    badge. Amber is "warning", "the key press" (the dashed line) and the rolling-window
    highlight. Green is "ok", "the chosen anchor" and "capturing". The same colour
    means different things in neighbouring elements.
14. **Two palettes that do not share a design.** Dark is "Obsidian Phosphor"; the
    light theme is called "Daylight Laboratory". They use different structures, so a
    user switching theme sees a different product, not the same one in another light.
15. **Button hierarchy is flat.** "Start", "Stop", "Scan 3D", "Watch" and "Mark
    Stutter" have different styles or none, and it is not obvious which one is the
    action the page is for.
16. **Empty states describe the interface, not the next step.** "NO INCIDENT MARK
    SELECTED: Start capturing (or click 'Simulate 120 FPS Stutter Trace' in the top
    right)..." sends the user to the top right corner for a demo, instead of making
    the first useful action obvious where they are.

## 4. Interaction and flow

17. **The core loop is hidden in the middle of the page.** The thing the user does
    (play, press a key when it stutters) has no dedicated place: it is a button row in
    the second card on the capture page, under the chart.
18. **Marking has no visible feedback on the page.** The audible tone is the only
    confirmation. A player with headphones off gets nothing, and the screen does not
    show the mark until the user goes to the workbench.
19. **Two ways to reach the same action.** "Simulate 120 FPS Stutter Trace" in the top
    bar and "Generate Simulated Mark" on the workbench do overlapping things with
    different wording.
20. **The export is at the end of a long page.** On the workbench, "Preview & Export
    Redacted Bundle..." is below the note field, after the candidate list. The user
    needs to finish every step before the one thing they may want, sharing, appears.
21. **Choosing the cause is explained in three places with different words.** The
    timeline says "NUMBERED BADGES = CANDIDATE SPIKES"; the list says "Pick the spike
    that caused your reaction (150-400 ms before key press)"; the investigator says
    "No event time was chosen. The moment the key was pressed was not used as the
    event time." Three explanations of one decision.
22. **Settings is a long page of mixed concerns.** Hotkeys, memory windows, sound,
    theme and the welcome screen share one scroll, with an "Apply & Save" button that
    covers all of them, including the hotkeys that need an explicit apply.

## 5. States and edge cases

23. **Loading and waiting are a single word.** "Awaiting stream", "Waiting for frames"
    and "Not capturing" are three different states shown with similar text, and the
    user cannot tell which one means "nothing is wrong".
24. **Errors come as one-line notices that fade.** A notice bar at the bottom shows a
    problem for a few seconds. A user who looks away misses a hotkey that did not
    register, which is exactly the kind of problem they need to see again.
25. **The welcome window is the best writing in the app, and it is the least
    reachable.** It appears once, and the only way back is "Show the welcome screen
    again" at the bottom of Settings. It explains what is read and what is never done,
    in plain language, which the rest of the app does not do.

## 6. What must be kept (the redesign may not drop these)

These are behaviour and safety rules the design must keep, not preferences:

- Hard constraints HC-1 to HC-13 (no hooks, no injection, no network, no telemetry,
  no ads, registry reads only). The interface may explain them; it may not weaken them.
- Provenance: every frame and every export says whether it is measured or simulated.
  The simulated badge stays wherever the data appears.
- "Unknown is not zero": missing values display as unknown (for example `--`), never 0.
- Key-press time is never the event time by default: the user chooses the anchor.
- Redaction and the export preview: the exact text is shown before anything is saved,
  and personal information removal stays on by default.
- Hotkeys are registered with Windows (`RegisterHotKey`); the interface never reads keys.
- Every existing function: capture start/stop, program scan, typed program name, marks
  from hotkeys and from the tray, candidate choice, adjusted time, note, export with
  preview, settings (hotkeys, pre/post-roll, sound, theme), capability report export,
  welcome screen, quit (stops capture).

## 7. Summary

The app is functionally rich and its safety design is careful. Its presentation is
written for the people who built it: internal codes, tool names, a tagline in the
smallest type, and three names for one product. The redesign's job is to make one
question answerable in the first ten seconds, "why did my game stutter?", and to keep
every rule above while answering it.
