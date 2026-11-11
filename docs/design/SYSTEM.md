# Design system

Status: specification, to be implemented in phase 2 pull requests. It is grounded in the resources
that exist on `main` today and in the measurements below. Where a value is proposed, it says so.

Read with `docs/design/DIRECTION.md` (the look and the phases) and `docs/design/PLATFORM.md` (the
module contract and the experience patterns). This document defines the tokens and components those
rules are built from.

---

## 1. What exists today (measured on `main`)

| Area | Current state | Problem |
|---|---|---|
| Colour | 40 brush tokens in `Themes/Dark.xaml`, the same 40 keys in `Themes/Light.xaml` | Good. Keep the keys and the two themes in step. |
| Views | No hex colour literals in any `Views/*.xaml` or `Views/*.cs` | Good. Keep it that way. |
| Font size | 14 distinct sizes in `Views/*.xaml`: 9.5, 10, 10.5, 11, 11.5, 12, 12.5, 13, 15, 16, 18, 19 (plus the default) | No type scale. Sizes differ by a half point, which the eye cannot use. |
| Font family | 42 inline `FontFamily="Consolas"` and 3 inline `"Segoe UI"` | The mono role has no token. Each use repeats the family. |
| Spacing | Margins and paddings are hand-set per element (for example 8, 10, 12, 14, 16, 18 mixed freely) | No spacing scale. |
| Styles | 15 shared styles in `Themes/Styles.xaml`: Heading, SectionTitle, CardTitle, Overline, KpiValue, MonoValue, Muted, PrimaryButton, SimButton, NavButton, PillRadio, Card, KpiCard, HeroCard, BadgeBorder (plus SlimScrollThumb) | Good base. Several sizes are set inline in the views instead of in the style. |
| Motion | None defined as tokens | Needs rules (section 5). |

The clipping fixed in phase 2 (#23, #25, #26) came from the spacing and type problems above, not from the colours.

---

## 2. Colour tokens

Keys stay as they are, so no view changes when a theme is edited. Groups:

| Group | Keys | Use |
|---|---|---|
| Surfaces | `WindowBackgroundBrush`, `SidebarBrush`, `PanelBrush`, `PanelRaisedBrush`, `PanelInsetBrush`, `CardHeaderBrush`, `KpiCardBrush`, `HeroBannerBrush` | Layers, from the window back to raised cards |
| Text | `ForegroundBrush`, `MutedBrush`, `SubtleTextBrush` | Primary, secondary, and tertiary text. Never for a value the user must read |
| Borders | `BorderLineBrush`, `BorderHighlightBrush` | Hairlines and the focused edge |
| Accent | `AccentBrush`, `AccentSoftBrush`, `AccentTextBrush`, `CyanAccentBrush`, `PurpleAccentBrush` | One primary accent. Purple is reserved for the mark on the Watch toolbar and must not be used for status |
| Interaction | `HoverBrush`, `SelectedBrush` | Hover and selected states |
| Status | `OkBrush` and `OkSurfaceBrush`; `WarningBrush` and `WarningSurfaceBrush`; `DangerBrush` and `DangerSurfaceBrush` | Meaning. A status colour is never the only signal: it appears with a word or a shape |
| Simulated | `SimulatedSurfaceBrush`, `SimulatedBorderBrush`, `SimulatedTextBrush` | The provenance badge for simulated data (HC-9) |
| Chart | `ChartBackgroundBrush`, `ChartTextBrush`, `ChartGridBrush`, `ChartLineBrush`, `ChartMarkBrush`, `ChartAnchorBrush`, `ChartGapBrush`, `ChartWarnBrush` | Charts only. Views outside charts do not use these |

Rules:

- A view refers to a brush by key. It never names a colour.
- Every key exists in both themes. A test should fail when a key is missing from one of them.
- Contrast: text on any surface meets WCAG 2.1 AA (4.5:1 for body text, 3:1 for large text and UI). This is checked when the palette changes.

---

## 3. Typography (proposed)

Five sizes, one family per role. Replaces the 14 sizes above.

| Token | Size (px) | Weight | Family | Use |
|---|---|---|---|---|
| `TypeDisplay` | 19 | Bold | Segoe UI | The one main number in a panel (KPI value) |
| `TypeTitle` | 15 | SemiBold | Segoe UI | Page headings, the workbench title |
| `TypeBody` | 12.5 | Regular | Segoe UI | Explanations, findings, body copy |
| `TypeLabel` | 11.5 | Regular | Segoe UI | Captions under values, card captions |
| `TypeMeta` | 10.5 | Regular | Consolas | Overlines, identifiers, units, timestamps, numbers in tables |

Rules:

- Monospace (`TypeMeta`) is for numbers and identifiers, never for sentences. The current use of Consolas on
  explanatory captions (for example the "BUDGET HAIRLINES" line) moves to `TypeLabel`.
- Numbers use tabular figures so live values do not shift.
- Upper case is used only for overlines and tags, at `TypeMeta`. Never for sentences.
- The sizes 9.5, 10, 11, 12, 13, 16, 18 are retired. Mapping: 9.5 and 10 become `TypeMeta`; 11 becomes `TypeLabel`;
  12 and 12.5 become `TypeBody`; 13 becomes `TypeBody`; 15 stays `TypeTitle`; 16 becomes `TypeTitle`; 18 and 19
  become `TypeDisplay`.

The scale is a proposal. The first implementation PR should check it visually against the Watch and Replay renders
before the other screens change.

---

## 4. Spacing and layout (proposed)

- Base unit 4 px. Allowed values: 4, 8, 12, 16, 24. Any other margin or padding is a review comment.
- Card padding 12 px; gaps between cards 8 px; page margin 16 px (the Watch and Replay pages already use 16 px).
- A card header is one row: title on the left, at most one caption on the right that is short enough to fit.
  A caption that does not fit moves to the line under the title. This rule fixes the collisions in #23.
- Grids use fractional columns (`*` widths). Fixed widths are allowed only for icons and buttons.

---

## 5. Motion (proposed)

- Durations: 120 ms for state changes (hover, selected), 200 ms for expansion (disclosure, panels). Nothing longer.
- Live values update in place without animation on every tick.
- Reduced motion (Windows setting) turns off every animation except state feedback.
- Motion never hides a value the user is reading.

---

## 6. Components

### 6.1 Existing, to keep (name and purpose unchanged)

| Style | Becomes |
|---|---|
| `Heading` | Page heading, `TypeTitle` |
| `CardTitle` | Card title, `TypeBody` semibold |
| `Overline` | Overline and tag, `TypeMeta`, upper case |
| `Muted` | Secondary text, `TypeLabel`, `MutedBrush` |
| `KpiValue` | Display value, `TypeDisplay` |
| `MonoValue` | Numbers and identifiers, `TypeMeta` |
| `Card`, `KpiCard`, `HeroCard` | Panel frame, with three levels: standard, emphasised (KPI), and page header (hero) |
| `PrimaryButton`, `SimButton` | Primary action and the simulated action (purple, labelled "Simulated") |
| `NavButton` | Sidebar item |
| `PillRadio` | Segmented choice (zoom, filter, theme) |
| `BadgeBorder` | Tag and badge |

### 6.2 New, from `PLATFORM.md` section 5.2 (built in phase 2, used by phases 3 and 4)

| Component | Parts | States |
|---|---|---|
| **Reading** | label, value (`TypeDisplay` or `TypeMeta`), unit, provenance badge, freshness, explain affordance | live, stale, paused, unavailable (with the reason) |
| **Live indicator** | a dot, a word (LIVE, STALE, PAUSED, OFF) | the four words above |
| **Status summary** | one sentence with a state word | normal, attention, unknown |
| **Finding card** | grade, claim, evidence, falsification test, next check | open, dismissed |
| **Action panel** | action name, risk statement, precondition results, consent control, rollback note | not available, ready, needs consent, done |
| **Comparison** | two values, the difference in words, the same axis | same, better, worse, unknown |
| **Alert** | severity word, source, threshold, start time | info, attention, critical |
| **Disclosure** | the three-level expander: Glance, Explain, Detail | collapsed, expanded |
| **Panel frame** | title, caption, actions, source state | the states of the source |

Every component takes its colours and type from tokens. It may not define its own.

---

## 7. Enforcement

Rules that a reviewer can check without running the app:

1. No hex colour, `FontSize`, or `FontFamily` literal in `Views/*.xaml` or `Views/*.cs`. Values come from tokens.
2. Every colour key exists in `Dark.xaml` and `Light.xaml`.
3. Margins and paddings use the allowed values in section 4.

Rules 1 to 3 should become repository lint checks in the same style as `ProductLints`. They fail CI, not review. The
lint is added in the first implementation PR, so the existing exceptions are listed and reviewed, not hidden.

---

## 8. Implementation order (phase 2 PRs)

1. **Tokens:** add the type and spacing tokens to `Themes/Styles.xaml`. Replace the inline `FontFamily` values. No visual change.
2. **Type scale:** map the 14 sizes to the five tokens, screen by screen, starting with Watch. Check each change in the render.
3. **Lint:** add the rules in section 7 to the repository tests, with the current exceptions listed.
4. **Components:** build the section 6.2 components, starting with Reading and Finding card, which the Replay screen needs most.
5. **Motion:** apply section 5, including reduced motion.

Phase 3 (Watch) and phase 4 (Replay) use the components from step 4.

---

## 9. Open questions for the owner

1. Confirm the five-step type scale in section 3, or give different sizes.
2. Confirm that purple is reserved for the mark (section 2), and that the simulated action stays purple.
3. Confirm that the lint in section 7 should fail CI (it changes merges), rather than only report.
