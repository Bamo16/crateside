# crateside design rules

What is built, for the next design session to start from. **Update this file in the same commit
as any change to tokens, chips, header shapes, table caps or shared controls.** The code wins where the two differ.

Everything else in this folder is frozen history from the Claude Design sessions: `canvases/`
(open a `.dc.html` in a browser with `support.js` beside it), `handoffs/`, `briefs/` and
`brand/`. [ARCHIVE.md](ARCHIVE.md) indexes them.

## Brand

Option 1a from handoff 04, interim: the **mark** (three crate slats edge-on, the middle one pulled
forward in brand amber `#e8b45a`) and the word `crateside`, always lowercase. The live assets are
`src/Crateside/wwwroot/brand/`, the one folder a brand swap changes; `docs/design/brand/` is the
frozen copy the Design session delivered.

- **Header**: `BrandNav` shows the 18px mark beside the word (15px/600), and the pair links to the
  Dashboard.
  The word stays HTML text, never the lockup, so it is selectable and on-token.
- **Browser tab**: `favicon.svg`, a 32px PNG fallback, the 180px apple-touch icon, and
  `theme-color` `#1b1a15`. Titles read `{page} · crateside`.
- **README**: the lockup on a dark GitHub theme, and `mark-tile.svg` on a light one (below).
- The mark's amber is brand amber, not *your turn*, and is never recoloured by page or state.

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
wins where it sets one.

The sample list and picker are `StemList` and `StemConfirm`, both driven by one `StemEditor`, and
are mounted by the Tracks row and by Track details.

## Side stack

The Tracks row's right column (340px) and Track details' rail (19rem) are one component,
`SideStack`, so the order cannot drift between them (handoff 06):

**Confirm** (`StemConfirm`, only while a tick has moved) → **Go** (Open folder · Track details) →
**Next** (the row's sentence, then Retry / Cut what is missing / Work this first) → **Tags** (genre,
familiarity) → **Name** → **Uploads** (rail only) → a stronger rule → **Start over** (Source, then
Remove). A group with nothing to show is not drawn. Findings stay full width above it.

- **Status + action.** A label line (label left, one or two buttons right), then the value, then
  the status as prose. A button never sits inside a sentence.
- **Tags.** Settled is one line with **Change**; unsettled, or Change pressed, shows every chip, and a
  pick folds it again. Not looked up yet is one quiet line with no button. Familiarity counts a
  derived value as settled, since it is what gets written.
- **Destructive.** Set apart by position and the stronger rule, not colour. Buttons stay `.outline`
  until the confirm step, which is `.danger`. Remove's confirm opens below its button.
- **Editors.** Rename and swap replace their group's body; the label turns amber and the groups
  below dim (45%) and go `inert`. The whole stack does the same under StemConfirm.
- **Parameters.** `TrackActions Part="Go|Queue|Remove"` with `ShowDetailsLink` and
  `ShowNextSentence`; `TrackEdits Part="Name|Source"`, sharing one `TrackEditing` so only one editor
  is open across both mounts. `SideStack OnTrackDetails` sets both flags and `Uploads` draws that
  group.
- The stack's vocabulary (`.group`, `.label-line`, `.stack-label`, `.stack-value`,
  `.stack-status`) is styled once, in `SideStack.razor.css` through `::deep`.
- **Two columns in the row** (handoff 07): the row's side is 680px and the stack wraps once, Go ·
  Next · Tags on the left, Name · Start over on the right. Below a 1200px viewport it falls back
  to 340px and one column. `SideStack Columns="2"`; the rail passes 1.
- **Folded while picking** in the row: after separation, or with a source form open, the side is
  340px and holds only StemConfirm and the add/adopt form (`SourceForms`), or a sentence and
  Close when nothing is ticked. `SideStack FoldWhilePicking`; the rail keeps the dimmed stack.

## Sample list

- **Closed**: one boxed line per sample (Sample · Ableton key · Where it is, and a note only where
  there is one), then the dashed *Change what is cut* opener. No samples yet is a dashed
  placeholder.
- **Picking**: bands side by side (`auto-fit, minmax(220px, 1fr)`). A line is tick · name (wraps,
  never truncated) · one state word in mono: *Staging*, *Library* (green), *new* (teal), *removed*
  (red, struck), *not made*. A preset's description is on hover. Before separation a line is tick
  and name only.
- *Add a source* and *Adopt a file* are compact dashed lines in the Sources band. In the row the
  form opens in the side column and its opener turns amber (*Adding a source →*); on Track details
  it opens inline in the band. `StemList FormsBeside` in the row.

## Tracks table

- A **Genre** column between BPM and State (150px). A suggestion is an amber dashed chip that
  settles it in one click; a settled genre is plain text; no suggestion is a *choose* chip that
  opens the row. `GenreStrip Shape="Cell"`.
- A **Tags to answer** filter: songs with an unanswered genre, or a familiarity the numbers leave
  to the user. Its rows show Song (with the genre note under it), every genre chip, and
  Mainstream / Obscure (`GenreStrip Shape="Chips"`, `FamiliarityStrip Inline`). An answered row
  stays in place at 45% until the view is left.
- A **Tempo drifts** filter (#75): tracks not warped yet whose tempo beatdetect reads drifting. The
  row carries a *tempo drifts* flag beside its title.

## Dashboard

Handoff 11, superseding Counts (handoff 08). The landing page and first tab (**Dashboard · Tracks ·
Tools · Settings**), at `/` and `/dashboard`; Tracks moved to `/tracks`. No width cap on the body.

- **Four sections, fixed order**: *Your turn*, *Working now*, *Over time*, *The library*. Not
  customizable. Each head (▾/▸, `h3`, a quiet gist) folds its section, and the fold is remembered
  per browser. **A folded section shows one summary line** in its place, so the page still says
  what is going on with everything folded; *The library* folds to its head, which carries its totals.
- **The index** (sticky, 168px) lists the four sections, then the library's panels indented 22px.
  *Your turn*'s count is amber when non-zero. Below 1100px it is a wrapping row.
- **Tiles**: an amber count is a link to the filter it counts (or the Settings row). A zero goes
  monochrome and says *none right now*; the tile stays, so the grid never shifts.
- **Working now** is five cards: Separation (with Pause), Judging (with Pause), Quotas (monochrome
  bars: neither working nor your turn), Background readers, Library backfill. Live, as the Tracks
  header is.
- **Over time**: tracks finished by ISO week (the current week dashed, "so far"), stages run, and
  the latest eight stage events.
- **The library grid** is three columns at ≥ 2000px, two below, one below 1400px. Key detectors
  and BPM detectors span two wherever there are two. Order: Key detectors, By key, By tempo, BPM
  detectors, By genre (with *What the sources called the rest* behind a dashed opener), By
  familiarity (with No key under it), By artist.
- **The action band** (*Never judged*) sits at the top of *The library*.
- **Top N · all.** Key shows 8 rows, artists 12, the all-three-wrong list 6.
- **Bar tables are teal.** The key table's minor (teal) and major (amber) bars are a known
  exception; a new bar table is teal only.
- **Split bars** (tempo bins, key detectors): the solid part is the strict count, teal 35% the
  looser one.

## Tools

Handoff 09. Small mashup helpers, one per sub-tab: **Partners · Re-Pitch**, each at its own route
(`/tools/partners`, `/tools/repitch`); `/tools` opens the last one used and `/partners` redirects.
The frame is `ToolsFrame`.

- **Sub-tabs** are 14px/500 `--text-60`, the current one `--text` with a 2px `--text` underline on
  the row's `--line-strong` rule, so they read apart from the header's 12px amber-underlined tabs.
- **A tool** is a lead sentence, the `.controls` box, a hint line, then its table. A new tool is one
  more sub-tab and route with the same anatomy.
- **The key is shared**: the one picked in a tool is the other's default until a restart, and both
  take `?key=`; Re-Pitch also takes `?bpm=`.
- **Re-Pitch is the one place BPMs show two decimals.** A BPM there copies on click.
- `--table-partners` stays the cap for tool tables; Re-Pitch's is 680px.

## Settings

Handoff 10. Every setting is one **row**: name (14px/500) and a one-line gist (`--text-60`) in a
260px left column; the control, then a one-line status (12.5px, `--text-60`) on the right; a
`--line` rule under it. Sections are 16px/600 `h3`s, 40px apart, in a 944px body.

- **Choices are `.pill` chips**, the chosen one `.pill.on.plain`. A click saves at once.
- **One sentence per status.** Arithmetic is a number ("288 of 10,000 quota units a day").
- **Folders are read-only**: name, the path in mono, and Open (or *Show in folder* for a file).
  They come from `CratesidePaths`.
- Rows that need a playlist are drawn disabled until one is saved.

## Page headers

`PageHeader` has three shapes, a closed set: **Bar**, **Flush**, **Shade**. A new page picks one.

| page | shape |
|---|---|
| Tracks | Shade |
| Track details | Bar, Tracks tab active |
| Dashboard | Bar |
| Tools | Bar |
| Settings | Flush |

## Table caps

One width cap per table shape: `--table-partners` 50rem, `--table-library` 56rem,
`--table-tracks` 92rem. Track details uses `--table-tracks`.

## Differs from the design

Where the build departs from a canvas, by page. Clear an entry once a later design supersedes it.

### README (handoff 04)

- **The lockup shows only on a dark GitHub theme.** Its ink is `#ece7dc` on transparent, which
  vanishes on white, and the brand rules allow no other colours. On a light theme the README shows
  `mark-tile.svg` (the mark on its own dark tile) above the text heading instead. A lockup for
  light backgrounds would be a Design question.

### Tracks row and Track details (handoff 06, #62)

- **Next keeps its colours**: the sentence is amber when ready to warp and full white when
  stopped, as before, where the canvas draws it in plain prose.

### Track details (handoff 05, #21)

- **Upload players show no still.** A still would be fetched from YouTube, so the box stays blank
  until pressed.
- **Stage chips are all coloured by state** (done green, running teal, your turn amber, stopped red),
  with the current stage the only filled, bold chip. **No timestamps** under them: the Activity log
  carries times. The user asked for both on 2026-09-25.

### Settings (handoff 10, #74)

- **No Separation or Judging rows.** Their pause controls already live in the Tracks header.
- **Folders also lists Ableton projects**, which postdates the design (ADR 0040).
- **Emptying as tracks are taken on** is disabled until removal access is granted, with a status
  saying so. The canvas only drew the authorized state.

### Dashboard (handoff 11, #78)

- **Tracks finished counts each track once**, in the week it was first tagged. The canvas drew the
  brief's figures, which counted tagging lines, so a re-tag counted twice (W37 read 199; it is 79).
- **The YouTube quota is an estimate**: units a day at the refresh interval, as Settings says it.
  Nothing counts the units actually spent, so there is no "today" and no reset time.
- **Judging has no progress bar**: it has no time estimate to measure against. Its card shows
  the elapsed time and the waiting count.
- **No "The whole log →" link.** The activity log is per track, on Track details; there is no
  page-wide log to open.
- **Key-detector figures are live**, read from the ledger on load and Re-read, so they move as
  verdicts arrive (406 HIGH or MEDIUM songs on 2026-09-28, against the brief's 397). A verdict
  recorded by hand carries no detectors, so its song uses the latest run that has them.
- **The BPM detector figures differ from the canvas** (from handoff 08): some detectors read fast
  as well as slow, so the fourth band is *Triplet or dotted* with its wider rule.
- **The shape line reads "Steady · Mostly steady · Drifting"** (handoff 08 drew the older names).

### Tools (handoff 09, #57, #58)

- ***Or from a track* searches the library's warped songs** (Counts' songs), not tracks still in
  staging.
