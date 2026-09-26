# crateside design rules

What is built, for the next design session to start from. **Update this file in the same commit
as any change to tokens, chips, header shapes, table caps or shared controls.** The code wins where the two differ.

Everything else in this folder is frozen history from the Claude Design sessions: `canvases/`
(open a `.dc.html` in a browser with `support.js` beside it), `handoffs/`, `briefs/` and
`brand/`. [ARCHIVE.md](ARCHIVE.md) indexes them.

## Palette

Defined once in `src/Crateside/wwwroot/app.css`. One warm dark palette:

| token | value | use |
|---|---|---|
| `--bg` | `#141310` | page |
| `--surface` | `#1b1a15` | boxes, bands |
| `--raised` | `#23211a` | buttons, inputs |
| `--ink` | `#0f0e0c` | text on a filled chip or badge, never a background |
| `--text` | `#ece7dc` | text |

Text tiers are alpha over `--text`: `--text-75`, `-60`, `-50` for prose; `-40`, `-35`, `-30` for
labels and decoration only, never a sentence.

## Colour means state, and nothing else is coloured

- **Teal**: the machine is working.
- **Amber**: it is the user's turn.
- **Red**: needs attention.
- **Green**: done, and only ever done.

A thing that is none of these is monochrome. The evidence's source-kind badges are the example:
filled for authoritative, outlined for corroborating, dashed for algorithmic and chords-only.

## Type

IBM Plex Sans for text. IBM Plex Mono for every number, key, timestamp and count.

## State chips

One component, `Components/StateChip.razor`, used wherever a track's state is shown. Styles:
neutral outline · teal filled with a dot · teal outline · amber filled · red outline · red filled
· green outline · dashed.

Track details' stage flow (`StageFlowStrip`) draws its own step chips: ✓ done (green outline) ·
● running (teal filled, bold, pulsing, the page's only animation) · ● your turn (amber filled,
bold) · ○ not yet (neutral) · – skipped (dashed, 55% opacity) · ■ parked (red outline) or gave up
(red filled, bold). The current stage is the only filled chip. No timestamps: the Activity log
carries them.

## Shared controls

Buttons (`.outline`, `.primary`, `.danger`), `.pill`, `.actions` and `.mono` are global, in
`app.css`, so a component mounted on two pages looks the same on both. A page's scoped rule still
wins where it sets one; Settings does, for its buttons.

The sample list and picker are `StemList` and `StemConfirm`, both driven by one `StemEditor`, and
are mounted by the Tracks row and by Track details.

## Page headers

`PageHeader` has three shapes, a closed set: **Bar**, **Flush**, **Shade**. A new page picks one.

| page | shape |
|---|---|
| Tracks | Shade |
| Track details | Bar, Tracks tab active |
| Library | Bar |
| Partners | Bar |
| Settings | Flush |

## Table caps

One width cap per table shape: `--table-partners` 50rem, `--table-library` 56rem,
`--table-tracks` 92rem. Track details uses `--table-tracks`.

## Differs from the design

Where the build departs from a canvas, by page. Clear an entry once a later design supersedes it.

### Track details (handoff 05, #21)

- **Rename and swap sit in the rail under Change**, stacked, not in a strip above the band. Brief 06
  (#62) is redesigning where actions live.
- **Genre in the rail is the row's full strip** (every genre as a button), not the amber suggestion
  box with *Use {genre}* / *Pick another*. A **Familiarity** strip sits under it (#52), which the
  canvas predates.
- **Upload players show no still.** A still would be fetched from YouTube, so the box stays blank
  until pressed.
- **Stage chips are all coloured by state** (done green, running teal, your turn amber, stopped red),
  with the current stage the only filled, bold chip. **No timestamps** under them: the Activity log
  carries times. The user asked for both on 2026-09-25.
