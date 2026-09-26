# Handoff 06: actions layout (row and Track details)

Answers `docs/design/briefs/06-actions-layout.md`. Canvas: `Crateside Actions Layout.dc.html` (1a–1g).
No new tokens, no new colours, only `.outline` / `.primary` / `.danger`.

## The decision

The row's right column (340px) and Track details' rail (19rem) are **the same side stack**, in one
fixed order. The row keeps its `1fr 340px` grid; the three full-width strips above the stems go.
The samples now start at the top of the panel.

Stack order (1a). A group with nothing to show is not drawn, label included.

| # | group | component | notes |
|---|---|---|---|
| 0 | Confirm | `StemConfirm` | only while a tick has moved; rest of stack dims to 45% and disables |
| 1 | Go | `TrackActions Part="Go"` | Open folder · Track details (row only). No label |
| 2 | Next | `TrackActions Part="Queue"` | row's "what happens next" sentence, then Retry / Cut what is missing (primary) / Work this first / Stop working this first (primary) |
| 3 | Tags | `GenreStrip`, `FamiliarityStrip` | one block, see below |
| 4 | Name | `TrackEdits Part="Name"` | status + action pattern |
| 5 | Uploads | `UploadCard` | rail only |
| — | rule | | `1px` at `rgba(255,255,255,.14)`, stronger than the group hairlines (`--line`) |
| 6 | Start over | `TrackEdits Part="Source"`, then `TrackActions Part="Remove"` | Swap source and Remove / Remove from playlist |

`TrackFindings` stays outside the stack: full width above the grid in the row, above the columns on
Track details. `StemList` keeps *Change what is cut · Add a source · Adopt a file* on its own label
line (same pattern as below).

## Parameters (one component, mounted per context)

- `TrackActions`: `Part` = `Go | Queue | Remove`; existing `ShowDetailsLink` (false on Track
  details); new `ShowNextSentence` (true in the row, false on Track details where the stage flow
  says it). Keep one `_problem` per mount.
- `TrackEdits`: `Part` = `Name | Source`. The one-editor-at-a-time rule must hold across the two
  mounts: lift `_renaming` / `_swapping` into a small shared per-track object (like `StemEditor`)
  and pass it to both. `Close()` closes both.
- Tag strips: `Collapsed` is derived, not passed (settled ⇒ collapsed unless Change pressed).
- Everything else is identical in both places. The rail does not stack differently from the row:
  both are the same single column.

## Status + action pattern (1b)

```
NAME                          [Revert] [Rename]     ← label line: label left, buttons right, min-height 28px
1991 – Seraph                                       ← value, 13px 500
renamed by you · was 1991 - Seraph (Official…)      ← status, 12px --text-50, wraps as prose
```

A button never sits in a sentence. The label line holds one word and at most two short buttons, so
it fits 19rem. No action available ⇒ empty slot, and the status says why (*a sample is in the
library, so rename it in Ableton*).

Source uses the same shape inside Start over: `SOURCE [Swap source]`, then *cut from the playlist
entry's own video* or *source swapped · cut from {url}* (url in mono). Library-only: Source is
not drawn.

## Tags (1b, 1d, 1e)

- **Settled**: one line. `GENRE` (label, 78px wide) · value 13px 500 · `[Change]` right. Familiarity
  adds its numbers on a line beneath, indented to the value (mono numbers).
- **Unsettled, or Change pressed**: label on its own line, all chips below (existing ChoiceStrip
  chips; suggestion stays amber dashed), note below the chips. Picking collapses it again.
- **Not looked up yet**: one quiet line, value in `--text-50`, no button.
- `ChoiceStrip.razor.css` loses `grid-column: 1 / -1` and `margin-bottom`; the stack owns spacing.

## Editors open (1f)

Rename / Swap source replace their group's body in place, full width of the stack: input,
sentence, detail, then `[primary or danger confirm] [Cancel]` on their own line. The group's label
turns amber and reads *Name · renaming* / *Source · swapping*. While either is open, the groups
below dim and disable.

## Destructive (1c–1e)

Start over is set apart by position and the stronger rule, not by colour. Its buttons are
`.outline` until the confirm step; the confirm is `.danger` (Swap source's confirm, Remove's
confirm). The Remove confirm opens **below** its button in a bordered box (`1px rgba(255,255,255,.1)`,
radius 8), sentence then `[Remove] [Keep]`.

## States on the canvas

- 1c row, a finding waiting (lookalike) · 1d row, ready to warp · 1e library-only, Remove confirm
  open · 1f rename open · 1g Track details rail with the picker confirm open.

## Spacing

Group padding `14px 0`, hairline `--line` between groups; Name group `14px 0 18px` before the rule;
Start over `padding-top:14px`, `gap:12px`. Buttons in a group: `gap 8px` (6px on a label line).
