# Test plan - Stage 1b (interactive slice)

Written **before** implementation (spec section 1 #3). Exit criterion (spec section 17): the owner validates on a real, non-protected game session (`docs/VALIDATION_1.md`). Before that stop, the agent proves everything it can without a person: unit tests for all logic, an end-to-end run of the real app against the synthetic harness, and mutation checks for every safety rule.

## Conventions
- Logic lives in `Jankscope.Core` (portable: chords, redaction, export model) and `Jankscope.Frames` (clock map, anchors, history, incident recorder, capture session). `Jankscope.Platform` holds only the Windows hotkey wrapper. `Jankscope.App` holds WPF, nothing else that needs a test to be believed.
- Harness data is `Simulated`; fixtures are `Fixture`. Neither is ever exported as a measurement.
- UI tests are few and run on an STA thread; no coverage chasing in XAML code-behind.
- Safety rules get a mutation check (break it, see a test fail, restore): canary survival, key-press never silently used as the anchor, chord validation, fail-closed export, no network type.

## Test matrix

| # | Area | Level | What must be proven | Example cases |
|---|---|---|---|---|
| 1 | Hotkey chord model (Core) | Unit | A chord needs at least one modifier and a supported key; text round-trips; reserved/unsafe chords refused; two actions cannot share a chord | `Ctrl+Alt+F9` parse/format; bare `F9` refused; `Ctrl+Alt+Delete` refused; lower-case and spaces; F1-F24, A-Z, 0-9; duplicate detection is case-insensitive; no key other than the chord is ever described |
| 2 | Hotkey registration (Platform) | Unit (seam) + integration | `RegisterHotKey` failures map to typed reasons; unregister on dispose; double registration of one chord by us is refused cleanly | Fake native seam returns 1409 (already registered) -> `AlreadyRegistered`; invalid key -> `InvalidKey`; real registration of an obscure chord succeeds and is released; second process owning the chord is simulated by registering twice |
| 3 | Capture clock map (Frames) | Unit + seeded | Maps a QPC instant to capture time with an uncertainty; never claims precision it lacks | Constant delivery delay -> exact offset, uncertainty ~ 0; jittery delay -> uncertainty = spread; no frames yet -> "unknown", not 0; clock backwards -> reset and flagged; mark before first frame -> unknown |
| 4 | Candidate anchors (Frames) | Unit + property | In the 10 s before the mark, finds the largest spike, stutter events, collector gaps; ranks and explains; offers "key press time" only as an explicit choice | Spike 1.2 s before the mark ranks first; spikes older than 10 s are not offered; no anomaly -> "no candidate found", never a fabricated one; gap in timestamps larger than the frame time -> gap candidate; ties ordered by time then size; capped list length; mark without a pick has `Anchor == null` |
| 5 | Incident recorder (Frames) | Unit + integration | Mark -> pre-roll window frozen, post-roll collected, window bounded; duplicate hotkey presses within the debounce window are one mark | 120 s pre-roll and 30 s post-roll on a synthetic stream; mark at stream start (short pre-roll) stated, not padded; two marks 0.2 s apart -> one; kind Stutter vs InputStall kept |
| 6 | Frame history (Frames) | Unit + concurrency | Ring of frames sized in seconds-of-history, not bytes; readers never torn; capacity bounded | Capacity from fps and pre+post roll with a hard cap; snapshot while writing; memory stays under the cap for a 10 min 360 fps stream |
| 7 | Capture session (Frames) | Integration with the fake PresentMon + unit | Frames reach history, detector, clock map; stop is graceful; anti-cheat gate honoured; health and drops visible | Fake exe streams 60 s -> counts match; fake exe stalls -> health says so; suppression check true -> start refused with the reason; stop twice is safe |
| 8 | Redaction v2 (Core) | Unit + canary + property | Username, hostname, SSID, BSSID, MAC, IPv4, IPv6, serial, command line and note text are replaced with category tokens; JSON-escaped paths and mixed case are caught; the redactor reports what it replaced | Canary of each category planted in every string field of the bundle -> none survive; `C:\\Users\\<name>` JSON-escaped; hostname inside a UNC path; command line keeps only the executable name; short (2-char) user names; unicode names; token is stable per value in one export |
| 9 | Export guard (Core) | Unit + mutation | Export fails closed when any known sensitive value survives; the preview shows exactly the bytes written | Disable one redaction category (mutation) -> guard throws and nothing is written; preview text equals file text byte for byte; user edits to the note are redacted too |
| 10 | Incident bundle (Frames/Core) | Golden + unit | Report + marks + anchors + frame excerpt + redaction summary, provenance labelled; synthetic/fixture data labelled; units on every number | Golden JSON for a synthetic incident; Simulated stays Simulated; unavailable data reads "not available (reason)"; frame arrays rounded; size bound for 150 s at 360 fps |
| 11 | Timeline model (Frames/Core) | Unit | Min/max bucketing keeps every spike visible at any zoom; time <-> x mapping round-trips; marks, anchors and uncertainty map to ranges | 100 000 points into 800 buckets keeps the max; zoom to a spike; single point; empty series; NaN never drawn as zero |
| 12 | WPF app smoke (App) | UI, STA | Window and tray start and stop; hotkey failure banner appears when registration fails; Mark buttons enabled only with a live capture; export dialog shows preview | Start app against a fake session; click Mark -> a mark row appears; fail registration -> banner text with reason; Quit stops the session and leaves no PresentMon child or orphan session |
| 13 | End-to-end (agent-run) | E2E, manual-scripted | Real app + real PresentMon + harness window with known hitches: marks, anchors, export preview and file all behave | Press the chord via a script after an injected hitch; the true hitch is among the offered anchors; exported file contains no canary; no network connection opened (checked with the OS socket table) |
| 14 | Safety and privacy | Static + unit | HC-1/HC-4/HC-5 still enforced as code grows | Existing lints stay green; no keyboard hook APIs; no key codes read; no network types in App; no `Process` use in App; no overlay window flags |
| 15 | Accessibility and DPI | Manual checklist + automation names | Keyboard navigation reaches every control; names set; contrast adequate; layout at 100/150/250 % | Tab order walk-through; AutomationProperties.Name on timeline, marks, buttons; screenshot at 100 % and 250 % |

## Order of work (TDD)
1. Core: chord model, redaction v2, export guard (tests 1, 8, 9).
2. Frames: clock map, candidate anchors, frame history, incident recorder, bundle (tests 3-6, 10, 11).
3. Platform: hotkey wrapper (test 2).
4. Frames: capture session against the fake PresentMon (test 7).
5. App: shell, tray, hotkeys, timeline, mark flow, export dialog, first run (tests 12, 15).
6. End-to-end run against the harness (test 13); static checks (14); mutation checks.
7. Independent code review (`docs/reviews/stage-1b.md`), fixes, re-verification.
8. `docs/VALIDATION_1.md`, STATUS/LIMITATIONS/CONTEXT updated; STOP for the owner.

## Out of scope for Stage 1b
Auto-flagged stutters and their rate limits (spec section 7.3; Stage 1b records only marks the person makes), Raw Input timing and the input-stall differential diagnosis (Stage 2; the "input stall" mark is recorded and exported, with "input data not available in this version"), network probes, drivers/WHEA rules, the elevated helper, any setting change (HC-7), Gaming Profiles, Benchmarks, the full ten-workspace shell (only what Stage 1b needs), localization.
