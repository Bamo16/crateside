# Handoff 20: Track details around the judge's text, a key strip and sharing

Brief 20, issue #150 (with #151 and #152), ADR 0064 · built against `43ca652`
Canvas: `Crateside Evidence And Sharing.dc.html`, live. **1a** is the evidence panel. Its pills
switch between *Starry Eyed Surprise* (plain), *Takyon* (through judge's windows), an old verdict
(one paragraph, no tonic check) and a WITHHELD with three keys open, and Share, *First pass*
and *Show all* all work. **1b** is the warp view as the track's timeline: window tags open their
drawer, `‹ ›` step through them, and there are pills for 8 bars, Starry Eyed, a song with sections
(invented) and the room left for chords and playback. **1c** is the share card for Starry Eyed, a
verdict with sections, and an old long reasoning cut short, at full size and at 36%.

The named tracks' readings, windows and recorded text are from the brief's screenshots. The
new-style texts (≤ 100-word reasoning, tonic checks, candidates, window whys) are the
designer's, written to ADR 0064's budgets from the judge's own arguments, so they are not
the judge's words. The withheld track, the song with sections, Takyon's key flicker after its
tenth change and Starry Eyed's six changes are invented.

**The rule:** the panel's left column holds the argument and its right column the evidence.
Windows leave the panel for the timeline, where each one opens from its band. The key strip
replaces *Key flicker*, drawing the verdict's centers as bands over a faint raw underlay. Share
sits at the head of the panel and draws a card of its own. Nothing is newly colored.

## 1. The evidence panel

The head line: `Why {key in full}` (17/600; `Why no key` for a WITHHELD), with the subtitle under it
(13, `--text-60`). When the run had windows, the subtitle ends ` · 3 judge's windows on the timeline ↑`
(underlined, `--text-75`), which scrolls to the warp view. **Share** sits at the head line's right
(§4). Headings spell the key in full now (*Why E major*); the band keeps `E maj`.

Two equal columns (`1fr 1fr`, gap 44px) under the head:

**Left (the argument):**
1. **Why this key** (caps label): `reasoning`, one `<p>` per paragraph, 14.5/1.62 `--text`, max
   62ch. An old run with no `candidates` and no `tonicCheck` shows a quiet line under the label (12,
   `--text-45` → use `--text-40`): *An older verdict: one paragraph, as the judge wrote it, through the formatter.*
2. **Considered** (caps), only when `candidates` is non-empty. One row per candidate, each with a
   hairline above: the key in mono 13.5/500 in a 92px column, then its sentence at 13.5/1.55
   `--text-75`. Mono tokens inside the sentence are drawn at `--text`. **A WITHHELD heads it
   *Still open***, because its keys are not rejected.
3. **Human sources**, as built.
4. **First pass: withheld**, only when the run has a first-pass reasoning: a fold under a
   `--line` rule, `▸ First pass: withheld · asked for 3 windows · 21 Oct, 12:01 AM` (12.5/500
   `--text-75`, with the tail in 400 `--text-50`). Open, the text is indented 18px at 13/1.6
   `--text-60`, quieter than the verdict because it is superseded. Drawn once, from the run. An old
   run's identical copied window reasons fold into this, per ADR 0064.

**Right (the evidence):**
1. **What the detectors reported**: column heads (11px `--text-40`): *Detector · Key · Score ·
   Band · Runner-up*, then one 32px row per detector on **one line**, with no wrap:
   `84px 88px 50px 70px 1fr`, gap 10. Name in Sans 13.5/500 `--text-85`, key mono 13.5/600, score
   mono 13 `--text-75` right-aligned, the band chip, and the runner-up mono 12.5 `--text-60`
   (`B major 0.740`, ellipsis if it ever overflows). The heads say *Score*, not *r*, since madmom's
   figure is not an r. Under the rows comes the backer line, as built, renamed (*…by both madmom and
   Essentia*).
2. **Tonic check** (caps), when there is one: 14.5/1.62 `--text`.
3. **Chord progression** (caps) with `70 chords · loops folded` (12, `--text-45` → `--text-40`)
   and a right-aligned underlined *Show all 70* / *Fold the loops*. Folded: mono 13/500 runs
   joined by `·`, with the count (` ×18`) in 400 `--text-50` and the separator in `--text-30`, so
   the runs read first. A run never breaks inside itself (`white-space: nowrap` per run). Open: the
   plain list in mono 12.5 `--text-75`.
4. **Tempo**, as built.
5. The *Recorded …* line, as built.

**Gone from the panel:** the *Windows* block and its cards, *Why the judge asked for these windows*,
the *whole file* fold (the whole-file detectors simply are the right column now) and *Key flicker*.
**Kept as built:** handoff 17's sections lines (every center other than the primary), in the left
column above *Why this key*. The canvas does not draw them. The key strip now shows where the
placed ones fall.

Narrow (below ~56rem of panel): one column, in the order left then right.

### The band chip

keydetect's flag becomes a chip naming the band: mono 10.5/500, 0 6px, radius 4, 1px
`--text` 30%, text `--text-75`. Its `title` carries the band's text:

| band | chip | title |
|---|---|---|
| r < 0.70 | `r < 0.70` | Unreliable band, r below 0.70: distrust |
| 0.70–0.75 | `0.70–0.75` | Middle band, r 0.70–0.75 |
| r > 0.75 | `r > 0.75` | Trust band, r above 0.75: watch for a relative-key swap |

The brief asked only for the distrust chip. I gave every band one, because *watch for a
relative-key swap* mattered on Starry Eyed. If the build would rather show only the distrust
band, drop the other two; the row keeps its 70px column either way. **No color**: a band is a
fact, not a state. Use whatever band names keydetect's flag actually carries; the middle band's
label above is a guess.

### Typesetting (the formatter's output, ADR 0064)

Keys, chords, Roman numerals attached to a chord (`B (V) → E (I)`) and r values in Plex Mono at
**.92em** so they sit on the Sans x-height, at the paragraph's ink. HIGH/MEDIUM/LOW/WITHHELD in prose:
`font-variant-caps: all-small-caps`, `letter-spacing: .05em`, uncolored. Nothing else styled.

## 2. The timeline: windows open from their bands

The warp view is unchanged above the lanes. Changes:

- **A window's tag on the ruler is a `<button>`** (`aria-expanded`). Clicking it **opens the
  window's drawer** and marks it selected: the tag filled `--text` with `--ink` text, and the band
  in every row stroked at `--text` 90% with a 7% fill. Clicking it again, the drawer's ×, or Esc
  closes it. One drawer at a time.
- **The drawer** sits under the time axis, 16px below, full panel width: `--surface`, 1px
  `--text` 16%, radius 8, with a 16×9 caret on its top edge at the band's center (clamped 12px
  inside the plot). Three columns, `250px 1fr auto`, gap 24:
  - left: the who-chip (*judge* dashed outline / *yours* filled, as handoff 18), the label 14/500,
    the span in mono 12 `--text-60` (`bars 28–36 · 1:53–2:20`), **the window's why** (13.5/1.5
    `--text`, one sentence; `Given by you.` for the maintainer's), then what it became at 12
    `--text-55` → `--text-50` (`→ section Chorus: E minor`, `→ no section: …`).
  - middle: the readings, in exactly the panel's detector row (§1).
  - right: × at the top; at the foot `‹ 2 of 3 ›` (`.outline` small, with the position in mono 11.5
    `--text-50`), which steps through the windows, moving the caret and selection.
- The selected lane's detail block still follows the warp view, as built. The drawer opens
  between the two.
- *Judge from these bars* (handoff 18) is unchanged. A recorded window is selectable once its run
  is recorded.

## 3. The key strip

A lane in the warp view **after the stem lanes and before the time axis**, on the same axis, so it
follows the zoom pills and ‹ › like every row. It is 52px high with a stronger top rule (`--text`
10%), because it is about the song, not a stem.

- **Gutter**: `Key` (12.5/500), then a gist in 10.5 `--text-45` → `--text-40`: `B minor, one
  center · faint: the running estimate, 27 changes`, or `A Dorian, with chorus sections from your
  windows`. Any unplaced section follows in mono 10.5 `--text-60`: `bridge · C Lyd: no time given`.
- **Bands: the verdict's centers.** Outlined boxes (1px `--text` 55%, radius 3, 3.5px from the
  top, 44px tall) with the short key in mono 11.5/500 `--text` at the top left (`B min`,
  `verse · A Dor`, `chorus · E min`). A label is drawn only where the band is 56px or wider, and the
  band's `title` always carries it. Fill `--text` 3%, or 7% for a band placed from a window.
- **Placement**, because a ledger `Section` has a label and a center but **no times**:
  - single center: one band across the track;
  - a section that came from a window takes **the window's span**;
  - **the primary center fills everything else**;
  - **a section with no time is not drawn**, only named in the gutter. Do not invent a placement.
  If section times arrive later (ADR 0064 leaves them undecided), the same bands take them.
- **The underlay: the raw running estimate.** It is drawn under the bands along the strip's foot:
  y 30–47, 17px. Each change is a block from its time to the next, filled `--text` at
  **alpha r × 0.22** (at most about .18), with a 3px tick `--text` 30% where it starts. A block 40px
  or wider is labeled in mono 9.5 `--text-42` → `--text-40` (`E min`). Each block's `title`:
  `0:03.39 · E min · r 0.36`. Zoomed out it is texture; at 8 bars it reads change by change. Raw
  keys use the short form.
- **Key flicker's fold goes.** The underlay is its replacement.

## 4. Room for what comes next (not built now)

- **Chord lane**: under the key strip, 40px, the same gutter shape (`Chords`, `with playback ·
  #105`). Its blocks will be labeled like the key bands. On the canvas it is a dashed placeholder.
- **Transport**: in the **ruler's gutter**, the one empty cell at the top left, replacing the `Bar`
  label: a 28px round play button (1px `--text` 45%, `aria-label="Play"`), then `0:47.20 / 2:48`
  in mono 11.5 (the total at `--text-40`). The zoom pills stay where they are.
- **Playhead**: a 1px `--text` line from the ruler's tick area down through every row to the strip
  (or chord lane), with a 10×7 head on the ruler. It is not teal: playing is not the machine
  working.

The rows above it need no change for either; the grid only gains a row.

## 5. Share

A **Share** button at the evidence panel head's right: `.outline` with a 14px stroke share
icon, `Share`, and a small `▾` at `--text-60`. It has `aria-haspopup="menu"`, and its background is
`#2e2b22` while open. It goes **here and not in the verdict band** because the card is this
panel's content, the band's *Why E maj ↓* already leads here, and the band stays four facts.

The menu is a 300px popover under the button, right-aligned, on `--raised` with 1px `--text` 16%,
radius 8, a soft shadow and `role="menu"`. Three items, each a title (13/500) over a hint (12,
`--text-55` → `--text-50`):

- **Copy image**: *The card, ready to paste into Discord or a text*
- **Save image**: the file name, `Paul Oakenfold – Starry Eyed Surprise.png`
- **Copy as text**: *Markdown, for an agent or a note*

A footer line under a hairline: *The card is 1200 px wide, on the app's dark palette. Its text is
the verdict's own, as on this page.* Choosing an item closes the menu, and a neutral
`✓ Card copied` / `✓ Saved to Downloads` / `✓ Copied as Markdown` (12.5 `--text-75`) shows left of
the button for about 2.4 s. It is not green: it acknowledges an action, it does not mean *done*.

Shown on any track with a verdict, WITHHELD included (its card says *WITHHELD* and lists the
keys still open).

## 6. The card

1200px wide, its height set by the content. It is its own layout, not a crop of the page, and uses
**the app's dark palette**: it is a picture of the verdict as crateside shows it, the color rule
needs no second palette, and Discord's usual dark theme would make a light card glare. Ground
`--bg` with a 1px `--text` 10% edge and radius 14, padded 56px 64px 44px, with sections 30px apart.

Top-weighted: **the first ~630px carry what a friend scans** in the preview.

1. `Artist – Title`: Sans 42/600, the artist at `--text-70` → `--text-75` and 500, then an en dash
   and the title at `--text`. Balanced wrap.
2. **The three facts** as the band shows them (short forms): a `--surface` box, radius 12, cells
   `1.25fr 1fr 1fr` with hairline dividers, padding 22px 28px. Caps labels 15px `--text-45` →
   `--text-40`. Key in mono 76/600 with the mode chip after it for a modal center (`A min` `Dor`:
   mono 24, 2px `--text` 50% outline, radius 6), nowrap. Confidence in mono 64/500. BPM in mono 76/600
   with its note under it (19, `--text-60`). No tempo is `—` at `--text-30` with *not measured*.
3. The subtitle: 24px `--text-75`.
4. **A verdict with sections** adds the key strip here: 56px bands, 2px `--text` 55%, radius 6,
   labels in mono 16, by the same placement rule as §3 (no underlay). Under it, 17px `--text-55` →
   `--text-50`: `Placed from your two chorus windows · bridge · C Lyd: no time given`.
5. **The readings**: one 52px row per detector, `170 190 100 150 1fr`, gap 18: name Sans 23
   `--text-80` → `--text-75`, key mono 25/600, score mono 23 right-aligned, the band chip (mono 18,
   2px outline), and the runner-up as `or C major 0.702` in mono 21 `--text-55` → `--text-50`. Then the
   backer line at 19px `--text-60`.
6. **Prose**: two columns (gap 48) when there is a tonic check, *Why this key* left and *Tonic
   check* right; otherwise *Why this key* alone, max 820px. 21/1.55 `--text`, with the same
   typesetting as §1 and 15px caps labels.
7. **Considered** (or *Still open*): three equal cells in a row, each with a top hairline: the key
   in mono 23/600 over its sentence at 18.5/1.5 `--text-75`.
8. Footer, under a hairline: the 30px mark, `crateside` (Sans 21/600), and `judged 24 Oct 2026` right
   (mono 17 `--text-50`). The mark's amber is the only color on the card.

Left off on purpose: the stage flow, the sample list, links, the windows, the progression and the
tempo sources. They mean nothing outside the app.

**Budgets and cuts.** New verdicts fit without cutting. An old long reasoning is cut, never
shrunk. The brief says to cut at a paragraph, but every old reasoning is a single paragraph (ADR
0064 found no newlines), so **the cut falls at the last sentence ending within ~110 words**, then
` …`. The type scale never changes to fit. A Considered list longer than three keeps the first
three (validation already caps it at 3).

**Preview size**: at 36% (about the width a chat gives an image in a message) the key and
confidence read, while the prose needs the image opened. That is the intended trade. Discord's
preview box for a tall image was not measured. Paste one real card into Discord before
settling the scale; type size is the only lever.

## 7. Copy as text

No design, but here is the shape, so it reads the same fields as the card:

```markdown
**Paul Oakenfold – Starry Eyed Surprise**
E major · MEDIUM · no tempo measured
No human sources; 3 detectors and the chord check.

| Detector | Key | Score | Band | Runner-up |
|---|---|---|---|---|
| keydetect | E major | 0.837 | r > 0.75 | B major 0.740 |
| madmom | E major | 0.983 | | |
| Essentia | E major | 0.811 | | |

**Tonic check.** The harmony is a G♯m → E loop …

**Why this key.** All three detectors name E major …

**Considered**
- G♯ minor: the relative. The loop starts on it, but nothing resolves to it.
- B major: keydetect's runner-up at r 0.740. …
```

In text the key is spelled in full, since a reader there has no band to give it context.

## 8. States drawn

1a: Starry Eyed (plain; Considered of two); Takyon (new texts, three Considered, First pass
folded and open); old verdict (Takyon's recorded 370-word reasoning through the formatter, no
tonic check, no candidates); WITHHELD (invented; *Still open* with three). Share menu open and
the acknowledgment. Progression folded and open.
1b: Takyon whole track (one band, the underlay showing E minor from 0:21 to 1:57 under the B minor
band); a window open with its drawer; 8 bars over bars 29–36 (the underlay labeled change by
change); Starry Eyed (one band, starter warps); sections (invented: verse A Dorian primary, two
chorus windows placed as E minor, a bridge with no time named in the gutter); chords and
playback room.
1c: Starry Eyed; sections; the old reasoning cut.

## 9. After building

Update `docs/design/DESIGN.md`: under *Warp view*, the window drawer, the key strip and its
placement rule; a *Track details* section for the evidence panel's two columns, the detector row
and band chip, Considered, First pass, the folded progression, Share and the card; and the
typesetting rule under *Type*. Clear the *Differs from the design* entries that the panel
replaces: handoff 18's *Window blocks sit in the measurements column*, and handoff 05's backer line
(now designed). Index handoff 20 and the canvas in `ARCHIVE.md` (done in this commit).

Some values above are written as `--text-45` → `--text-40` (and similar). The canvas used an
in-between alpha; build the right-hand tier, which is the one the palette has.
