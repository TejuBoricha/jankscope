# Design direction: "Replay"

## The one question

The product answers one question: **why did my game stutter?** Every screen, label
and motion should serve the user in answering it in the first ten seconds. Everything
that does not help answer it moves out of the way.

## The idea

A stutter is a moment, not a statistic. So the product is built around **the moment**,
not around the instruments. The user plays, presses one key at the moment they feel the
hitch, and then sees that moment replayed: the frames around it, the frame that hurt,
and what the program chose (or not) to show. The analysis tools exist, but they serve
the replay.

Working title for the metaphor: **a replay booth**. You stand in it, press a key when
something goes wrong, and it plays back the seconds before. Nothing in the interface
should look like a dashboard for an engineer.

## Structure: follow the user's task

Navigation is organised as the user's journey, with four steps in one order:

1. **Watch** — choose the program, start, play. Shows live frame timing as a single
   calm line. The primary action is "Mark it" (the hotkey, and a large button).
2. **Replay** — the moments the user marked, as a list of replays. Opening one shows
   the window of frames around the mark (the workbench, redesigned as a replay).
3. **Share** — the export with the preview, presented as "what leaves your PC": the
   exact text, the removed personal information and the file.
4. **Tools & trust** — Capability check (was "Radar"), Input lab (was "Input polling
   lab"), Settings, and About and privacy (the constraints, written for people).

The sidebar names are the user's words: Watch, Replay, Share, Tools, Settings, Trust.
The developer names move into the pages' secondary text.

Naming, decided here (not left open):
- **The product is "Jankscope"** everywhere. "JANK//SCOPE" and "Studio" are dropped.
  This follows the name in the docs, the welcome text and the export.
- **"Input stall"** is the one name for the event, in every surface and in the export.
- **"Mark"** is the one name for the user's action. "Stutter" is a kind of mark, not a
  separate concept.
- **"PRO" is removed.** The About page says there are no feature gates; a badge that
  implies otherwise is a contradiction in the product.
- **HC codes leave the interface.** They stay in the docs and tests, where they are
  checked. The About page says the rules in plain words and links the proof.

## Visual language

- **Calm surfaces, one signal.** Neutral, low-contrast surfaces (no glow, no radar
  motifs). One signal colour means one thing: **the frame that hurt**. Amber is the
  marked moment, green is "chosen", red is used only for an error that stops something.
  The mapping is fixed and written down in the tokens, so the same colour never means
  two things.
- **Numbers in a mono face, words in a humanist sans.** The mono face is only for values
  (frame times, percentages, the export text). Explanations are in the sans, at a
  readable size (no body text below 12 px).
- **Few containers.** A page has one primary surface (the timeline or the replay) and
  text around it. Cards are used only where a list of things needs separating.
- **Both themes are one design.** The light and dark themes share every structure and
  differ only in the colour tokens. "Obsidian" and "Daylight" names go; the theme is
  "Dark" and "Light", as the system uses them.

## Motion

Motion explains cause and effect, and nothing else:

- A mark **pulls the cursor** on the replay to the frame it was made at, so the user
  sees where the key press landed.
- The **chosen moment** holds still when chosen; candidates that are not chosen fade back.
- A **new frame** enters the live line from the right at the rate it arrives; no
  animation when nothing changes.
- Motion is off when Windows asks for reduced motion.

## Feedback and states

- A mark is confirmed **on the screen**, not only by a tone: the marked frame appears
  on the live line at once, with the word "Marked" beside it.
- Every waiting state says what it waits for: "Waiting for the program to draw frames
  (it may be in a menu)", not "Awaiting stream".
- Errors **stay on the page** until the user dismisses them or the cause is fixed. A
  hotkey that could not register is a persistent line with the reason and the fix.
- Empty states point to the next action **on the same page**.

## First use

The first screen is the Watch step, with one sentence and one button: "Play a game,
press Ctrl+Alt+F9 when you feel a stutter, and we will show you what the frames did."
The welcome text (which is the best writing in the app) becomes a short, skippable
page with the same content, and is reachable from Trust at any time.

## Accessibility

- Every control has a UI Automation name and works from the keyboard; the Watch step's
  mark button is reachable without the mouse.
- Contrast meets WCAG AA for text in both themes (checked, not assumed).
- No information is carried by colour alone: marks have a shape and a number as well.

## What this direction deliberately does not do

- It does not add features. Every function in `AUDIT.md` section 6 stays.
- It does not add a web view, an ad slot, a network call or any element that reads
  input beyond the hotkeys.
- It does not use a third-party design or animation service. Everything is in the
  repository, so it builds and tests in CI with the rest of the product.

## Delivery in phases

Each phase is its own pull request. The render step added in #19 produces PNGs for
every run, so each phase can be looked at before the next.

0. **This document** (audit and direction).
1. **Language and structure.** New names, sidebar order, term consistency, HC codes
   removed from the interface, the PRO badge removed. No visual change beyond labels.
   Tests that assert on the old wording are updated, not deleted.
2. **Tokens and components.** The colour, type and spacing tokens for both themes,
   and the button, nav and surface styles, applied everywhere.
3. **Watch.** The live capture page, with the mark as the primary action and a visible
   confirmation.
4. **Replay.** The workbench redesigned around the replay, the candidate choice and
   the timeline.
5. **Share, Tools, Settings, Trust, Welcome, Export.** The remaining pages and windows.
6. **Motion and states.** The motion rules above, loading and error states, and the
   accessibility pass.

## What I need from you

Nothing blocks phase 0 or phase 1. Two things are yours to decide, and I have chosen a
default for each:

- **The name.** Default: keep "Jankscope" and drop the "JANK//SCOPE" branding. Change
  it if you want a different product name; it is one string per place.
- **Whether to keep a paid edition badge at all.** Default: no badge.

I can see the screens only through the render artifacts (PR #19). Send me the PNGs of
each phase, or confirm the artifact is enough, and I will review against this document.
