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
- The mark's amber is brand amber, not *your turn*, and is never recolored by page or state.

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

## Color means state, and nothing else is colored

- **Teal**: the machine is working.
- **Amber**: it is the user's turn.
- **Red**: needs attention.
- **Green**: done, and only ever done.

A thing that is none of these is monochrome. The evidence's source-kind badges are the example:
filled for authoritative, outlined for corroborating, dashed for algorithmic and chords-only.
The warp view's provenance chips are the second: filled for the user's warp, outlined for one
borrowed from a sibling, dashed for a starter, Live's unsaved analysis or no warp yet; amber only
while waiting on Live, teal only while crateside writes.

## Type

IBM Plex Sans for text. IBM Plex Mono for every number, key, timestamp and count. A tilde and
weight 400 mark an approximate figure, and nothing else.

**The judge's text is typeset, not colored** (ADR 0064, handoff 20). In prose a key, a chord, a
Roman numeral attached to a chord and an r value stay in the body face at weight 600 in `--text`;
Plex Mono is for tables and the chord progression only. HIGH, MEDIUM, LOW and WITHHELD in prose are
all-small-caps with `letter-spacing: .05em`, uncolored. Nothing else in a sentence is styled.
`ProseText` does it; `Mono="true"` sets the same spans in Plex Mono for a table cell.

## Tempo

Handoff 13, ADR 0049. Every BPM is a tempo and **one formatter writes it** (`TempoFormat`); a test
fails if a page formats one itself. Summaries go through `Bin` and `Range` and carry no marks.

- **Two marks, used together.** Vouched: the bare figure at 600 (headline) or 500 (secondary), two
  decimals with `.00` dropped (`128`, `128.02`, `119.50`), never one. Approximate: `~` and weight 400,
  always a whole number. The ledger's estimate is approximate and also `--text-50`. No tempo: `—` at
  `--text-30`.
- **States.** Vouched: any warp that holds, or a reading whose windows agree within 0.02%.
  Approximate: a reading that agrees less tightly. Drifting: `~` the median. Mostly steady and
  changing: the main tempo, vouched. The ledger's estimate: only with no warp and no reading.
- **The note**, also the tooltip wherever a figure stands without one: shape first, then source,
  joined by ` · `. `drifts 110–112 · 2% · from your warp`, `steady for 91% · one part changes tempo ·
  from your warp`, `changes tempo · 24 bars at 132 · from your warp`, `crateside's reading · steady
  to the hundredth`, `crateside's reading · steady, but not to the hundredth`, `ledger's estimate ·
  not read yet`.
- **Three source phrases only**: `from your warp`, `crateside's reading`, `ledger's estimate`; as a
  label, `Your warp`, `crateside's reading`, `ledger's estimate`.
- **Drifting**: the range is the slowest and fastest window, rounded outward (`127–129`, never
  hundredths); the spread is that range over the figure shown, as a whole percent, `<1%` under one.
  A warp's windows are four audible bars, read from its bar lines; a reading's are its 30 s windows.
- **Track details**: the verdict band's BPM cell is the 52px headline with its weight and the note
  under it. A drifting tempo adds the **tempo strip** under the note: 44px, one `--text-75` tick per
  window, placed by its time and between the rounded range, a dashed `--text-35` line at the figure shown,
  range labels left and times below (mono 10.5, `--text-40`), a tick's title `2:30–2:38 · ~112`.
- **A warp off its tempo wears a pennant** (handoff 16, #96), after the figure and nowhere else:
  filled (`--text` at 80%) from 10 ms by the end, hollow (1px `--text-60`) below. It means one thing,
  *crateside can fix this for you*, so `steps in its grid`, `grid not confirmed` and a reading that
  merely disagrees get none. In a Tracks BPM cell it sits in the invisible `.00`'s slot, 4px after
  the figure (7×9, the column stays 84px); on Track details it is 11×14, top-aligned. `OffMarkGlyph`.
- **Track details names both figures** under the note, 13px `--text-75`: `Your warp is typed at
  125. The audio holds 125.017, so its grid falls 27 ms behind by the end.` with a *True up in Warp ↓*
  link; hollow: `…: 4 ms by the end, too little to hear.`; the other states say why there is no
  retype. The words live in `TempoFormat.Explanation`.
- **The evidence panel's Tempo block**: the headline at 20px and its note, then a ruled line per
  other source: label, figure (mono 13, `--text-75`, decimal-padded in 64px) and how it relates
  (*agrees*, *half of it*, *double it*, *two-thirds of it*, *three-quarters of it*, *not warped
  yet*, *not read yet*). Figures that fit no relation say *conflict* in `--red`; a known relation
  is never red.

## Warp view

Handoff 14, `WarpView`. A **Warp** section in Track details' main column under the sample list,
open, no fold, drawn when the track has samples.

- **One time axis for the group**: the samples cut from the upload most of them share, in the
  sample list's order; the warp guide is left out. Grid of 200px gutter · 12px · the plot.
- **Head line**: `WARP`, then `{n} stems · one warp · {markers} · {mode}`, or `{stem} warped on its
  own` at `--text` (the only loud thing; not colored). Zoom pills right: *Whole track · 32 bars ·
  8 bars*; zoomed, `bars 61–68 · 2:10–2:27` and `‹ ›` move by half a view.
- **Rows**: overview strip (zoomed only, click to move) · ruler (loop bar, bar ticks, clip-start
  notch labelled `clip start` in the bar-number row, which takes the place of any number it would
  run into; bars numbered as Live's ruler numbers them, from the shared warp's beat 0 as bar 1, with
  -1, -2… before it and no bar 0, across the whole file, ADR 0072) · tempo line (step line from the shared
  markers, dashed at the headline figure, ±1 BPM when steady, the rounded range when drifting) ·
  **Off the grid** (below) · one 64px lane per stem · time axis.
- **Off the grid** (handoff 16, #96): drawn for a steady warp whenever the check ran, never for one
  that changes tempo. A 72px plot, a dashed zero line and one dot per 10 s window at the median
  offset of its transients from the warp's grid, joined by a 1px line, ±30 ms, early up. The gutter
  names the drift (`27 ms by the end`, `holds`, `steps in its grid`, `too few transients`) and
  carries **True up**, which acts on the track's canonical warp, so selecting a lane never changes
  it. The status line under it takes the states offered, confirm, writing, done and *Not offered:
  …*. The milliseconds are drawn here only, never on the lanes.
- **A lane**: flags (7×9 pennants) where every visible pair is ≥ 9px apart, else 9px ticks with
  faint lines; Live's overview at 42%, stereo as two channels; transients only when zoomed;
  the whole file at full strength, the stem's clip start a 1px `--text-50` line (nothing dimmed
  before or after the clip, ADR 0072); a stem with markers of its own boxed dashed from where
  it parts. No sidecar: a dashed box with one line (amber while waiting for Live).
- **Markers are the ones Live shows**: Live writes one more, 1/32 beat past the last, and that one is
  never drawn or counted. A steady warp is *1 marker*.
- **The selected lane's detail**: file name and chip, seven facts (Tempo · Mode · Markers ·
  Transients · Clip · Loop · Default clip), the provenance sentence, then the two controls in the
  side stack's status + action shape under a stronger rule. A control that does not apply keeps its
  label and says why at `--text-40`. Confirms are `.danger`; the asking label turns amber.
- Wording lives in `WarpWording` (Core); times in the audio read `0:14.20`, dates `29 Sep`.
- **Judge from these bars** (handoff 18, #116): a small `.outline` opener reading *Judge again* after the head line's
  gist, on a track with an upload, hidden while the form is open or a run goes. The form sits
  between the head line and the ruler in a `--surface` box (1px `--text` 12%, radius 8): the label
  amber, the unit note `--text-50`; one row per range (number · range 120px mono · label 160px ·
  note · ×), a dashed *+ another range*, then *What you hear* (key, mode select, the Ear setting's
  hint) and the cost line beside `.primary` button (*Judge from these bars*, or *Judge the whole track again* while no range is filled in) and Cancel. No confirm. While
  it is open the ruler grows to 44px, takes a crosshair and snaps a drag to whole bars (whole
  seconds without a warp, whose ruler counts time). The band itself follows the drag in the
  browser, snapped as it goes, and the range is sent once, on release; each range has a handle on
  either edge. **Bands**, one per window, in every row from the ruler down: yours solid at 50% with a
  6% fill, the judge's dashed with no fill, the one being read teal; the tag sits on the ruler
  (`1 · 33–48`, `1 · read`, `Chorus · C♯ min`, `judge · bars 57–72`). Running, the form folds into
  a teal-bordered line: step chips, the step in teal, *You can leave…* right. Done, a neutral line
  says what was recorded; withheld again or failed reopens the form with its ranges.
- **A window opens from its ruler tag** (handoff 20, #150): the tag of a recorded window is a
  `<button>` (`aria-expanded`); clicking it fills the tag `--text` with `--ink` text and strokes
  the band in every row at `--text` 90% with a 7% fill, and opens one **drawer** under the time
  axis, 16px below, full width: `--surface`, 1px `--text` 16%, radius 8, with a 16×9 caret on its
  top edge at the band's center (kept 12px inside the plot). Three columns `250px 1fr auto`: who-chip,
  label, span (`bars 28–36 · 1:53–2:20`), the window's why (`Given by you.` for yours) and what it
  became; the readings, in the evidence panel's detector row with its heads; × and `‹ 2 of 3 ›`.
  Clicking the tag again, ×, or Esc closes it. `WindowDrawer`.
- **The key strip** (handoff 20): the last lane before the time axis, 52px, on the zoomed axis, with
  a stronger top rule (`--text` 10%). The gutter says `Key` and a gist (`A Dorian, 3 centers, placed
  from your windows`), then each section with no time in
  mono: `bridge · C Lyd: no time given`. **Bands** are the verdict's centers, outlined (1px `--text`
  55%, radius 3, 44px), the short key in mono 11.5 where the band is 56px or wider (`verse · A
  Dor`), the title always carrying it; fill 3%, 7% where a window placed it. **Placement**
  (`KeyStrip.Plan`): one center is one band across the track; a section from a window takes the
  window's span; a section with a `Start` runs from it to the next boundary; the primary center
  fills everything else; a section with none of these is named in the gutter and never drawn. **A
band whose start only the judge gave** (no window) has a dashed outline and the title says `start
not placed by a window or source`. The running estimate is not drawn: keydetect's key values are
running totals, so a change is where the whole-song estimate flipped, not where the key does (#174).
`KeyLane`.
- **The band invites it** only where the verdict is WITHHELD, or LOW with the whole-file detectors
  split: the Confidence cell adds the reason and an `.outline` *Judge from these bars ↓* that scrolls
  here and opens the form. Running, the cell holds the teal chip `● judging from bars` and
  `{spans} · {step} · started {time}`; recorded, `judged from bars 33–48, 82–96 · just now`.

## Track details

Handoff 20, `Evidence` (#150). The section's head is `Why {key in full}` (17/600; `Why no key` for a
WITHHELD) over a 13px `--text-60` subtitle that ends `· 3 judge's windows on the timeline ↑`
(underlined, scrolls to the warp view), with **Share** at the right. Two equal columns, gap 44px,
one column below 56rem of panel (a container query).

- **Left, the argument**: the sections lines, **Why this key** (14.5/1.62, max 62ch; an older verdict
  gets a quiet line under the label), **Considered** (or **Still open** for a WITHHELD): a key in
  mono in a 92px column, then its sentence at `--text-75`; **Human sources**; and **First pass:
  withheld**, a fold under a `--line` rule, the text indented 18px and quieter than the verdict.
- **Right, the evidence**: **What the detectors reported** with column heads (*Detector · Key ·
  Score · Band · Runner-up*) and one line per detector (`DetectorRows`), the backer line, **Tonic
  check**, **Chord progression** (`70 chords · loops folded`, `Show all 70` / `Fold the loops`; folded
  runs in mono 13/500 joined by `·` with the count in 400 `--text-50`, a run never breaking
  inside itself), **Tempo** and the *Recorded …* line.
- **The band chip** is keydetect's flag as a fact, not a state: mono 10.5/500, 1px `--text` 30%,
  radius 4, no color, its tooltip the flag's own band (*unreliable band*, *the gray zone*, *usually
  right, but watch for a relative-key swap*).
- **Gone from the panel**: the Windows block (they open from the timeline), *Why the judge asked
  for these windows*, the whole-file fold and *Key flicker* (the key strip's underlay).
- **Share** sits in the head: `.outline`, a share icon, `Share ▾`. It is a disabled placeholder
  until #152 builds the menu and the card.

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

**Refresh** is `RefreshButton`: a 13px ↻ icon, `--text-50`, after the "checked … ago" text it
refreshes. While its refresh runs it turns teal and spins (no spin under reduced motion). Every
"check now" or "re-read" is one of these, never a text link (user, 2026-09-29).

**Times of day follow the machine's locale** (`"t"`, or its pattern beside an ISO date), so 12-hour
with AM/PM here. Never a hard-coded `HH:mm`.

The sample list and picker are `StemList` and `StemConfirm`, both driven by one `StemEditor`, and
are mounted by the Tracks row and by Track details.

**A range of shifts is a row of chips**, −2 … +2 (handoff 19, Partners): 38×30, radius 6, mono 12.5,
off on `--raised` with a 1px `--line` (the `0` chip at `--text-75` as the anchor), in the span
`--text` at 9% with a 50% border and 600, the two ends at 18% so the span reads as a bar. With one
shift chosen, clicking another stretches the span to it; with a span, a click collapses it to that
chip. After the chips, the span in mono (`0 only`, `−1 to +2`) and the count; under them a hint
saying how clicking works. Each chip carries `aria-pressed`.

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
- **Destructive.** Set apart by position and the stronger rule, not color. Buttons stay `.outline`
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

- *Where it is* reads `Sample Staging · warped` for any stem with a finished warp; which sibling a
  warp came from is said by the warp view's chip, not here.
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
- A **Tempo drifts** filter (#75): tracks not warped yet whose tempo beatdetect reads drifting.
- An **Off its tempo** filter (handoff 16, #96) beside it: steady warps whose grid falls away from
  the audio, the furthest first. Columns Song · Key · BPM · **Runs** (`0.017 slow`, mono 13
  `--text-75`) · **By the end** (`27 ms`, mono 13/500, right; under 10 ms muted) · State, with a gist
  right of the pills. The title chip for this state is gone: the pennant by the BPM replaces it.
- **The BPM column is 84px, aligned on the decimal point**: right-aligned, and a whole figure
  carries an invisible `.00`, so the tilde hangs to the left. 600/14px (16px roomy), 400 when
  approximate; amber on a ready-to-warp row, approximate figures included. The cell's title is
  the tempo's note.
- **Tempo flags** beside the title, neutral, after *name pending*: *drifts N%* on any drifting
  tempo, warped or not (the spread in mono), *part changes tempo*, *changes tempo*.
- A **name pending** neutral flag beside a title StemForge has not given yet: the row shows the
  upload's name (the uploader before a title that names no artist) until it does.
- **Opening and closing a row keeps your place** (handoff 13): what you clicked stays still on
  screen, the row's header, or the top of its file list for anything inside the panel. The panel's
  height eases over 220ms (`grid-template-rows` 0fr ↔ 1fr, `cubic-bezier(.33,1,.68,1)`), with no
  motion under reduced motion. An opened panel scrolls into view only as far as its bottom plus
  16px, never taking the header above 8px; a close is held by a spacer after the list that only
  shrinks as you scroll up, and a filter or search resets it. `Tracks.razor.js`.
- **The expanded panel has no heading**: the file list starts with its column heads. *Re-cut
  pending* leads the side column's note while picking, and before separation the *changes apply
  now* hint ends the `.detail` line. Track details keeps its heading (`StemList Headed`).
- **The picker is the same list** (handoff 13b): one column of `.stem` lines under the same
  column heads, in bands Vocal · Instrumental · Whole track · Other sources (then *No longer
  offered*). Lines on disk stay the same elements and only move down; band labels, unmade
  presets and the Add a source / Adopt a file openers grow in (0fr ↔ 1fr, 220ms, fading in after
  66ms), and the *Change what is cut* opener grows out. The tick column is always there, empty
  until picking. The side column stays: while picking, only its action block and Next are swapped
  for the lead sentence, the change summary and Confirm / Cancel. The picker's anchor is the
  column heads.

## Dashboard

Handoff 11, superseding Counts (handoff 08). The landing page and first tab (**Dashboard · Tracks ·
Tools · Settings**), at `/` and `/dashboard`; Tracks moved to `/tracks`. No width cap on the body.

- **Five sections, fixed order**: *Your turn*, *Working now*, *Activity*, *Over time*, *The library*. Not
  customizable. Each head (▾/▸, `h3`, a quiet gist) folds its section, and the fold is remembered
  per browser. **A folded section shows one summary line** in its place, so the page still says
  what is going on with everything folded; *The library* folds to its head, which carries its totals.
- **The index** (sticky, 168px) lists the five sections, then the library's panels indented 22px.
  *Your turn*'s count is amber when non-zero; *Activity*'s is the number running, teal, followed by `●`, and absent while idle. Below 1100px it is a wrapping row.
- **Tiles**: an amber count is a link to the filter it counts (or the Settings row). A zero goes
  monochrome and says *none right now*; the tile stays, so the grid never shifts.
- **Working now** is five cards: Separation (with Pause), Judging (with Pause), Quotas (monochrome
  bars: neither working nor your turn), Background readers, Library backfill. Live, as the Tracks
  header is.
- **Activity** (handoff 21, #189) is one table, one row per action: a stage's start and finish
  joined on the job id and ordered by start, so a row never moves (a running row is replaced in place
  when it finishes). Fixed columns (`16px 72px 92px 300px 1fr 92px`: status, Started, Stage, Track,
  Result, Took) under `--table-activity`. Rows sit in buckets (*Last 10 minutes*, *Earlier today*,
  *Yesterday*, then dates); a run of one track shows a rule instead of repeating the name. States:
  teal pulsing dot and wash while running (*Took* ticks, *so far*), green ✓ for 10 minutes then
  grey, red ✕ and `failed: …` kept red, grey – for canceled. The result is the job's `summary`
  (a failure's `error`), as Track details' activity log words it, and a click opens the whole text
  with its meta line and links. Stage pills combine (All clears them), *Failed or canceled* narrows;
  page size 12/25/50/100 and *Failed or canceled* are remembered in the `crateside.dashboard.activity`
  cookie, the page is not. Page 1 is live; a later page holds still behind *↑ N new since you
  paged away*. Under 1100px of the box's own width each row stacks. Folded, it says what runs, the
  last finish and today's failures (a red link that unfolds it filtered).
- **Over time**: tracks finished by ISO week (the current week dashed, "so far") and stages run, two
  equal columns under `--table-activity`.
- **The library grid** is three columns at ≥ 2000px, two below, one below 1400px. Key detectors,
  Loudness adjustments and How the starter warps fared span two wherever there are two. Order: Key
  detectors, Loudness adjustments, By key, By tempo, How the starter warps fared, By genre (with *What the sources called the rest* behind a dashed opener), By
  familiarity (with No key under it), By artist.
- **The action band** (*Never judged*) sits at the top of *The library*.
- **Top N · all.** Key shows 8 rows, artists 12, the all-three-wrong list 6.
- **Bar tables are teal.** The key table's minor (teal) and major (amber) bars are a known
  exception; a new bar table is teal only.
- **Split bars** (tempo bins, key detectors): the solid part is the strict count, teal 35% the
  looser one.
- **Tempo summaries are whole with no marks**: bins and peaks (a peak counts songs whose headline
  rounds to it), and *By artist*'s range, rounded outward. *How the starter warps fared* lists
  song names under a count, with no figures.

## Tools

Handoff 12, superseding handoff 09's sub-tabs. **One page at `/tools`, no sub-tabs**, in two
columns: *Working on* (340px) with the **Camelot wheel** under it, beside **Re-Pitch** (foldable)
above **Partners** (never folds), which share one column of up to 860px. The state lives in the query (`?track=`
and `&section=`, or `?key=a-dorian&bpm=`, plus `&shift=` and `&lo=&hi=`), so a reload or a pasted
link restores it, and the tab reopens where it was left until a restart.

- **The selected track** is a clickable card (song, the verdict's key with the arrow for a mode,
  `A Dorian → A min · 8A`, the formatted BPM with its weight and its short note, genre ·
  familiarity · Track details). Clicking it turns it into the search, open and focused; Esc or clicking
  away puts it back. Under it, the key and BPM by hand; **editing any of them drops the track** and keeps
  what was picked. The key is **two selects**: *Tonic* (72px, mono, sharps as the rest of the app
  spells them) and *Mode* (`major`, `minor`, then an optgroup *Modes*: Dorian, Phrygian, Lydian,
  Mixolydian, Locrian). The mode starts at the track's center, or `major`.
- **Re-Pitch rows are selectable**: the chosen shift is 600 weight on `rgba(255,255,255,.08)` with
  a 1px inset `--text-35` ring and a *→ Partners* tag. **This is the house style for a chosen row
  in a tool table.** Clicking it again returns to 0. The BPM cell copies instead of selecting.
- **Folded, Re-Pitch** is a dashed summary line above Partners, and its shift stays in force. The
  fold is remembered per browser, in a cookie so the first paint is already folded; the default
  is open.
- **Partners' basis box** (handoff 19) says which center it works from: caps *Working from*, then
  for a mode the lead in mono 15/500 with `→` at `--text-60`, the tag 20/600 and its Camelot. Under
  it the source in one sentence (*The track's verdict.*, *The key you picked.*, *Re-Pitch −1 from
  the track's A Dorian.* with *Back to A Dorian*, or which section), and for a mode or a mixed
  verdict `ModeText`'s reason. **No relative pill**: the relative is the *same notes* row at shift 0.
- **Section pills** sit in the basis box under a hairline, one per section in ledger order
  (`verse A Dor primary`), when the verdict has sections. The chosen pill is the house *chosen*
  style for pills: `--text` at 14% fill, 50% border, 600. The primary is chosen by default; picking
  a section makes its center the source and returns Re-Pitch to 0.
- **Partners' table** has *Key · Camelot · Shift · Notes shared · Alt*, in two groups, *As is ·
  shift 0* and *A shift away · pitch the sample by Shift to land on {source}*; an empty group is not
  drawn. Rows sort by |shift|, then notes shared. At shift 0 only, a key carries one mark: **same
  key** (filled pill, `--text` 85%, `--ink` text) or **same notes** (the same pill outlined); a
  modal source's tag row says *the tag* in plain `--text-50`. Neither is colored: they are facts.
  A modal row's Camelot is its tag's, at `--text-40`. **Notes shared** is seven 8px pips (held
  filled `--text` 85%, the rest a 1px `--text-30` outline) and the figure `6 of 7` in mono, `7 of 7`
  at `--text`. Shifts carry a real minus.
- **The wheel** is monochrome: working-from filled `--text` (a mode fills its tag's segment),
  mixes-as-is at 22%, a shift away dashed, and the track's own key ringed when a Re-Pitch shift is
  in force. Read-only.
- **Re-Pitch's *Lands on*** names the center, mode included (`G♯ Dorian`), with its tag's Camelot.
  It keeps its ±N slider: it lists every shift's BPM to pick one row, which a span would not help.
- **Breakpoints**: below 1700px *Working on* runs across the top and the wheel moves to the right of
  the tools (300px); below 1100px one column with the wheel last. The wheel is never drawn inside
  Partners: beside its short basis box it left a hole (2026-11-04).
- **A third tool** joins the tools column with Re-Pitch's anatomy: above Partners if it changes the
  key or BPM Partners works from, below it if not.
- **Re-Pitch figures use the tempo formatter**: a shift of a vouched tempo is vouched, a drifting
  track's are `~` whole numbers. A BPM copies on click; **the clipboard gets the figure without the
  tilde**, and the row says what was copied (and that it's approximate, for a drifting track).
- **A drifting track's BPM field** holds the whole figure, with `~` as a fixed prefix outside the
  editable text, and the hint says every figure is approximate. Typing drops the track; a typed
  figure counts as vouched.

## Settings

Handoff 10. Every setting is one **row**: name (14px/500) and a one-line gist (`--text-60`) in a
260px left column; the control, then a one-line status (12.5px, `--text-60`) on the right; a
`--line` rule under it. Sections are 16px/600 `h3`s, 40px apart, in a 944px body.

- **Choices are `.pill` chips**, the chosen one `.pill.on.plain`. A click saves at once.
- **One sentence per status.** Arithmetic is a number ("288 of 10,000 quota units a day").
- **Folders are read-only**: name, the path in mono, and Open (or *Show in folder* for a file).
  They come from `CratesidePaths`.
- Rows that need a playlist are drawn disabled until one is saved.
- **Your ear** (handoff 18) sits in *Work* beside the backfill: *Hint* · *Human source*, read by the
  judge at the start of each run.

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
`--table-tracks` 92rem, `--table-activity` 97.5rem (1560px; the dashboard's Activity table and Over
time). Track details uses `--table-tracks`. The Tools column caps itself at
860px, Re-Pitch and Partners alike.

## Differs from the design

Where the build departs from a canvas, by page. Clear an entry once a later design supersedes it.

### Partners by notes (handoff 19, #125)

- **The shift chips run −2 … +2, not −6 … +6.** Every key is within ±2 of a partner at its
  smallest shift, so a chip past ±2 could only list keys at shifts the *Alt* column already shows
  (user, 2026-10-17).

- **Keys are spelled with sharps** (`C♯ major`), in the table and the tonic select, as everywhere
  else in the app; the canvas spelled majors flat. The wheel keeps its own labels.
- **The primary section is the one whose center is the verdict's**, where the canvas took the
  first. When no section holds it, none is marked primary and none is chosen until clicked.
- **A track with no verdict** pairs from its samples' Key tag, and says so: *The track's Key tag:
  it has no verdict.*
- **"Clockwise first" in the sort counts any clockwise step**, where the canvas counted only the
  next segment. It only orders rows tied on everything else; both of brief 19's tables come out
  the same.

### Mode beside its tag (handoff 17, #114)

- **No time strip in *Why this key*.** The centers are the ruled lines with their labels; the strip
  on the track's time axis waits for the judge to write section times (#111). The covers chip and
  the relation line are built.
- **The sections in Track details list every other center.** A center equal to the primary is not
  repeated, so a song whose sections all share the tag shows no sections block.
- **The Off Its Tempo list keeps its 90px Key column and shows no abbreviation**, as it never showed
  more than the tag.
### Judge from these bars (handoff 18, #116)

- **The cost line gives no share of the week.** Nobody has measured what one session costs of the
  weekly quota, so it says the tokens only. The 85% sentence is added when the guard holds.
- **One note per range is kept as one direction.** The ledger has one `Direction` per run, so the
  notes are joined under their windows' names (a single window keeps its note bare). A window's
  block in *Why this key* says *Given by you.* rather than whether it had a note.
- **WITHHELD keeps the band's red weak tone**, not `--text-50`; the invitation, its reason and the
  running and recorded states are as designed.
- **Bands are drawn row by row**: each row draws the band's sides and fill, with no top or bottom
  edge, rather than one rectangle over the grid.
- **Enter in the last field adds a line but does not move focus to it.**
- **A window in another center is named in the recorded line** (`Vocal: G major.`) rather than
  counted as not agreeing, since it is a section of the song, not a dissent.
- **Ruler tags run past a narrow band** rather than being cut to its width.
- **The form runs with no ranges**, judging the whole track again, so a plain re-judge needs no
  window.
- **The form needs the full mix**: the working set's, else a source kept in the library. Without
  either, the cost line says crateside downloads it again first, without separating it, and the run
  shows a *Downloading* step.

### Track details evidence (handoff 20, #150)

- **Keys in prose are not mono.** The canvas sets them in Plex Mono at .92em; the maintainer wanted
  prose to read as prose (Takyon's reasoning read like code), so they are the body face at 600 and
  `--text`. Mono stays in tables, the Considered key column and the progression.
- **The band chip's text and titles are the flag's own** (`r < 0.70`, `r 0.70–0.75`, `r > 0.75`;
  *unreliable band*, *the gray zone*, *trust band*), not the canvas's guessed names. The Band column is
  86px, not 70, for `r 0.70–0.75`.
- **First pass: withheld has no date.** The ledger keeps the first pass's reasoning but not when it
  ran, so the tail says only `asked for 3 windows`.
- **A section with a `Start` runs to the next boundary** (the ledger has starts, not ends). Sections
  with neither a start nor a window are named in the key strip's gutter, as designed; runs judged
  before section times have only the windows' placements.
- **A detector table read after the verdict says so**: one muted 13px line under it, *Read on
  keydetect 1.2.1, after this verdict.* (*Read after this verdict.* where the build is not one
  crateside knows). The re-read (ADR 0066) and the backfill (#139) write readings into a finished run,
  so the judge's prose can quote a reading the table no longer shows.
- **Share is a disabled button**, with no menu, card or text export until #152.
- **Not built, as the handoff says**: the chord lane, the transport and the playhead.
- **Your note to the judge and your ear** keep their place, heading the right column, which the
  canvas does not draw. The *Centers* lines stay above *Why this key*.

### Warp view (handoff 14, #97)

- **Marker counts are the markers Live shows**, one fewer than the canvas: the brief's "two
  markers" counted the hidden one Live writes past the last. A steady warp reads *1 marker*.
- **Flags or ticks is decided at the 832px plot** the page has at its width cap, not measured in the
  browser.
- **The overview strip is dragged, not only clicked**: pressing jumps there and holding slides the
  view, in hundredths of the track (100 targets, not a measured position), each view starting on a
  bar line.
- **Re-apply is also offered on a starter** when a sibling holds a warp of the user's, as for Live's
  analysis; lending would replace it within a minute anyway.
- **"Putting the warp back"** reads *Live wrote a fresh file. crateside puts the warp back within a
  minute.* while it lasts; the worker puts warps back once a minute.

### README (handoff 04)

- **The lockup shows only on a dark GitHub theme.** Its ink is `#ece7dc` on transparent, which
  vanishes on white, and the brand rules allow no other colors. On a light theme the README shows
  `mark-tile.svg` (the mark on its own dark tile) above the text heading instead. A lockup for
  light backgrounds would be a Design question.

### Tracks row (handoffs 13, 13b, #85)

- **A song cut from more than one upload shows one line per preset**, with the other upload's
  file as a dashed line under it, where the closed list used to give each file its own line.
- **One row is open at a time**, as before; the canvas allows several. Opening a row while another
  above it closes still keeps the clicked header still.

### Tracks row and Track details (handoff 06, #62)

- **Next keeps its colors**: the sentence is amber when ready to warp and full white when
  stopped, as before, where the canvas draws it in plain prose.

### Track details (handoff 05, #21)

- **Upload players show no still.** A still would be fetched from YouTube, so the box stays blank
  until pressed.
- **Stage chips are all colored by state** (done green, running teal, your turn amber, stopped red),
  with the current stage the only filled, bold chip. **No timestamps** under them: the Activity log
  carries times. Both were asked for on 2026-09-25.

### Tempo (handoff 13, #92)

- **Two more relations in the evidence panel**: *four-thirds of it* and *one and a half times it*.
  The handoff's list left them out, which would have made a known relation red.
- **The tempo strip ends at the end of the last window**, not the track's length, which crateside
  does not keep beside the tempo. A warp's ticks are four bars wide, not 30 s.
- **The *Working on* card puts key · Camelot and the tempo on lines of their own**, the figure and key
  at full ink and the note in Sans, instead of one mono string that wrapped mid-phrase (user,
  2026-10-02).
- **Off its tempo (handoff 16, #96, ADR 0050), where the build differs from the design:**
  - The pennant's outline is drawn by hand (a notched 7×9 polygon); the canvas's exact path was not
    copied.
  - The design's `--text-55` for an under-10 ms *By the end* is `--text-50`: there is no 55 tier.
  - The done line reads `Trued up just now: 125.017, holding within 5 ms all through the track. Your
    previous warp is backed up.` The build does not keep the residual or the old figure, so it says
    the 5 ms it requires instead of `within 1 ms`, and not `(125, saved 29 Sep)`.
  - The confirm names the real source of the phase (`the drums`, or the stem's own transients).
  - A warp view row is drawn with the check's own windows (10 s). Whether a longer window reads
    straighter, and where a step jumps, are open: see the issue.
- **A part that changes tempo is always *one part***, never *the intro* or *the outro*; the warp's
  reading does not yet say where the change is.
- **A warp's drift range is measured over windows of four audible bars**, a variant of the
  handoff's first option. Markers inside a bar do not change its length, so a cluster of them in a
  fill cannot spike a window; a downbeat marker 25 ms off moves four bars' tempo about 0.3%, against
  1.3% for one bar. *Rapper's Delight* reads 110–113 · 2%, *Hits from the Bong* 91–93 · <1%. Real
  slowdowns stay in: *Jump* 100–135 (one bar at 3:02 reads 69), *Hate To Say I Told You So* 94–141
  (its last four bars slow down).

### Settings (handoff 10, #74)

- **No Separation or Judging rows.** Their pause controls already live in the Tracks header.
- **Folders also lists Ableton projects**, which postdates the design (ADR 0040).
- **A *Starter warps* section** (Off · On the grid, with a note under the pills; *Tempo only* was removed on 2026-10-19) sits
  before Emptying the playlist. It was added without a design, from the existing row and pills.
- **Emptying as tracks are taken on** is disabled until removal access is granted, with a status
  saying so. The canvas only drew the authorized state.

### Dashboard (handoff 11, #78)

- **Tracks finished counts each track once**, in the week it was first tagged. The canvas drew the
  brief's figures, which counted tagging lines, so a re-tag counted twice (W37 read 199; it is 79).
- **The YouTube quota is an estimate**: units a day at the refresh interval, as Settings says it.
  Nothing counts the units actually spent, so there is no "today" and no reset time.
- **Judging has no progress bar**: it has no time estimate to measure against. Its card shows
  the elapsed time and the waiting count.
- **The Judging card says why it holds or stopped**, as a `status` line under the Paused chip:
  *Held: <reason>. Trying again at 4:15 PM.* while the endpoint refuses, and after five refusals in
  a row *Paused by crateside after 5 refusals in a row: <reason>. Resume to try again.* (#181).
  Resume clears the count. A track the endpoint refused reads `judge refused` on its chip and
  *judge refused: <reason> · next try 4:15 PM* (*· judging paused* when paused) in its Next line.
- **Background work** (the canvas's *Background readers*) also shows the download and key-detection
  stages, the track each is on or *idle*: they had no place on the canvas (user, 2026-09-29). The
  beatdetect row reads *BPM detection*, beside *Key detection*.
- **The fold is kept in a cookie**, not local storage, so the server renders the page already
  folded and there is no flash of open sections on reload.
- **The title row also checks the playlist**: *Playlist checked … ago* and a refresh button, as
  Tracks has, and it wakes the download stage so a new entry starts at once. The library counts'
  *Re-read* is a refresh button too.
- **The Claude quota has its own refresh**, under its bar: *resets at … · checked … ago* ↻. It is
  also re-read once a minute.
- **No "The whole log →" link.** The activity log is per track, on Track details; Activity's
  filters and pager (handoff 21) answer it, still with no separate page.
- **Key-detector figures are live**, read from the ledger on load and Re-read, so they move as
  verdicts arrive (406 HIGH or MEDIUM songs on 2026-09-28, against the brief's 397). A verdict
  recorded by hand carries no detectors, so its song uses the latest run that has them.
- **The BPM detectors panel is gone** (ADR 0075). It had one detector, beatdetect, and said little;
  *How the starter warps fared* took its place. The ledger stopped producing a BPM (#93); older ledger lines
  keep their `bpm` as the last-resort figure.
- **The shape line reads "Steady · Mostly steady · Drifting"** (handoff 08 drew the older names).
- **Key detectors shows the three detectors crateside still runs**: keydetect, madmom and edma (#113).
  CREMA and librosa are retired from the panel and its miss table (their readings stay in older
  ledger runs, untouched). A song counts as *all three wrong* when all three read it and all miss.

### Activity (handoff 21, #189)

- **Running text is just *running*.** The design's *1 of 3 files written* and *turn 4 on
  deepseek-web* need the files on disk and the judge session, which the job record does not carry.
- **Held and waiting-for-you rows** (judging held by the endpoint, a verdict awaiting the person)
  have no state in the design. They are drawn like a cancellation (grey –) with `held: <reason>` or
  *waiting for you*, and *Failed or canceled* leaves them out. A held judging that repeats its
  reason is listed once.
- **A finish with no recorded start** (older lines) shows its finish time as the start and *—* for
  *Took*.
- **A running record never closed** (the app died, and nothing adopted it) stays *running* until a
  later line for the job closes it; a new start for the same job that was not an adoption replaces it.
- **The page is held with the newest start's position in `jobs.jsonl`**, not its time, so two
  starts in the same instant cannot slip past it.
- **Over time's folded line** no longer names the last event: Activity's does.

### Tools (handoff 12, #79)

- **No redirects from `/tools/partners`, `/tools/repitch` or `/partners`.** No aliases that
  nothing uses (2026-09-28), and nothing links to them.
- **The track is picked from warped library songs**, as the Re-Pitch search already did.
- **Re-Pitch sits above Partners, in one column**, not in a column of its own between *Working on*
  and Partners: folding it shifted Partners and the wheel sideways (user, 2026-09-29). It keeps
  its 540px width.
