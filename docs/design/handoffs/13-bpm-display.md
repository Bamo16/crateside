# Handoff 13: how a BPM reads (brief 13, issue #92, ADR 0049)

Canvas: `Crateside BPM Display.dc.html` (new), turn 1, options 1a–1f. Built against `58a2dae`.
Song names and BPM figures in the canvas come from the brief and the ADR. Keys, genres and the Dashboard counts are made up for illustration.

Numbering: this repo's `handoffs/13-row-motion.md` and `13b-picker-layout-fix.md` were given brief 13's number by mistake on the Design side. This file is the handoff for **brief 13**. Commit it as `handoffs/13-bpm-display.md` and leave the others where they are.

## The system (1a)

Two marks, the same meaning everywhere:

- **Vouched**: the bare figure at weight **600** (headline) / **500** (secondary). Two decimals, with `.00` dropped and any other hundredths kept. Never one decimal.
- **Approximate**: a **tilde** (`~`, U+007E, no space) and weight **400**. Always a whole number, so a tilde never appears with hundredths. Both marks are always used together, so either one alone is enough on an amber row or a copied screenshot.
- **keytag's estimate**: approximate, and also drawn at `--text-50`.
- No tempo: `—` at `--text-30`, as built.

Shape and source are said **in words**, never with a third mark.

| state | figure | weight | Tracks flag | note / tooltip |
|---|---|---|---|---|
| Vouched, whole (warp) | `128` | 600 | — | `from your warp` |
| Vouched, whole (reading) | `174` | 600 | — | `crateside's reading · steady to the hundredth` |
| Vouched, hundredths | `128.02` `119.50` | 600 | — | `from your warp` (or the reading's line above) |
| Approximate | `~123` | 400 | — | `crateside's reading · steady, but not to the hundredth` |
| Drifting | `~111` (median) | 400 | `drifts 2%` (turn 2) | `drifts 110–112 · 2% · from your warp` / `· crateside's reading` (turn 2) |
| Mostly steady | `124` (main tempo, vouched) | 600 | `part changes tempo` | `steady for 91% · one part changes tempo · from your warp` |
| Changes tempo | `180` (main tempo, vouched) | 600 | `changes tempo` | `changes tempo · 24 bars at 132 · from your warp` |
| keytag's estimate | `~128` | 400, `--text-50` | — | `keytag's estimate · not read yet` |

Note grammar: **shape first, then source**, joined with ` · `. The same string is the song page note **and** the tooltip anywhere a figure appears without a note (Tracks cell, Dashboard rows).
- `steady for N%`: the main tempo's share of the track's length. Leave it out when the warp doesn't give it.
- `one part`: replace with `the intro` / `the outro` if the build knows where the changing part is. Otherwise keep `one part`.
- `24 bars at 132`: the second-longest stable tempo. If there are more than two, use the second-longest one and add ` · and others`.

Source phrases, the only three: `from your warp`, `crateside's reading`, `keytag's estimate`. A secondary line's label is the same phrase capitalized: `Your warp`, `crateside's reading` (crateside stays lowercase), `keytag's estimate`.

### Formatter
One entry point that returns `{ text, approximate, note, flag }` for a tempo. Pages render `text` with the weight and ink set by `approximate` and the source, and nothing else. Two more methods so summaries stay covered by the no-hand-formatting test:
- `Bin(lo, hi)` → `124–127`
- `Range(lo, hi)` → `85–128`. Lowest headline rounded down, highest rounded up. When `lo == hi` (one song), it falls back to the formatted tempo.

Summaries carry no marks.

## Each place

### Tracks column (1b)
- **Aligned on the decimal point**: right-aligned, and a whole figure gets an invisible `.00` (`visibility:hidden`, same font) after it. The tilde hangs in the gap to the left.
- Column width: 80px → **84px**.
- `.bpm` keeps 600/14px (16px roomy), and approximate is 400.
- Ready-to-warp rows stay amber, approximate figures included.
- keytag figures use `--text-50`. They can't land on an amber row.
- The BPM cell's `title` is the state's note.
- **Title flags** (existing neutral flag style):
  - The drifting flag reads `drifts N%` (turn 2, 2c) and now shows on **any** drifting tempo, warped or not. The *Tempo drifts* filter is unchanged: not warped yet, drifting.
  - New flags: `part changes tempo` (mostly steady) and `changes tempo`.
  - Order beside the title: *name pending* first, then the tempo flag.

### Track details: verdict band BPM cell (1c)
- 52px mono headline, weight per the table, with no invisible padding.
- The `.sub` note under it is the state's note.
- The red conflict note is replaced: conflicts now show in the evidence comment (below). The `.sub` line keeps the `conflict` class (red) only when the note ends ` · conflict`.

### Evidence panel: Tempo block (1d)
- Headline line: figure at 20px with its weight, then the note (12.5px, `--text-60`).
- Under it, one ruled line per **other** source: label (Plex Sans 12.5, `--text-60`, 150px) · figure (mono 13, `--text-75`, 500 vouched / 400 approximate, right-aligned and decimal-padded in 64px) · comment (12.5, `--text-50`).
- The headline's own source isn't repeated.
- Comment words: `agrees` · `half of it` · `double it` · `two-thirds of it` · `three-quarters of it` · `not warped yet` · `not read yet`. When two figures fit none of those relations, the comment is just `conflict`, in `--red`. A known relation is never red. (In 1d, Smash Mouth's keytag `~104` is a made-up conflict example.)
- These replace `beatdetect 63.5 · half or double`, `beatdetect … · conflict` and `no cross-check`. No one-decimal figures anywhere.

### Dashboard (1e)
- **By tempo**: bins and peaks are whole numbers with no marks. A peak counts songs whose headline rounds to it (128.02 counts as 128). Drifting songs count at their whole figure.
  - Gist: "Each song's headline BPM: the warp where there is one. Bins and peaks are whole numbers."
- **BPM detectors drill-down**: one track per row, so each figure is formatted with its marks. *Your warp* uses the warp's formatter, *Read* uses that detector's tempo (keytag `~`, beatdetect by its own spread). Decimal-padded, right-aligned. The ratio stays `0.00`.
  - Copy keeps the tilde: it's a report, not a value to paste.
  - Band rules (`within 0.5` …) are unchanged.
- **By artist**: `Range`, whole. With one song, the formatted tempo (`119.50`).

### Tools (1f)
- The *Working on* card shows the formatted figure with its weight, then ` BPM · {note}` (shortened to the source for vouched figures: `from your warp`; drifting: `drifts 110–112`; mostly steady / changing: `part changes tempo` / `changes tempo`). This replaces "whole-number warped BPM".
- **BPM field**:
  - Vouched: the figure as formatted (`119.50`, `128`), not `128.00`.
  - Drifting: the whole figure, with `~` as a fixed prefix (`--text-50`) **outside** the editable text, and a line under it: "The tempo drifts, so every figure here is approximate. Typing in the field drops the track."
  - Typing drops the track (as built) and the tilde goes with it. A typed figure counts as vouched.
- **Re-Pitch rows use the formatter**:
  - A shift of a vouched tempo is vouched: two decimals, `.00` dropped (so 0 on a whole track reads `128`).
  - A drifting track's rows are `~` whole numbers at weight 400.
- **Clipboard**: exactly the figure on screen without the tilde. Feedback in the row: `Copied 126.61`, or for drifting `Copied 105. The tempo drifts, so it's approximate.`
- Tools only picks warped songs, so approximate and keytag figures don't appear there.

## Turn 2: how much a drifting tempo drifts (2a–2c)

Applies to the drifting state only. Where it differs, this section overrides the table above.

- **Range**: the lowest and highest tempo, the lowest rounded down and the highest rounded up. So it's never a single number: 127.6–128.4 reads `127–129`. Never hundredths.
- **Spread**: `(highest − lowest) ÷ the figure shown`, from the raw figures, as a **whole percent**: `2%`. Under 1% reads `<1%`. No tenths: the inputs are 30-second windows or warp segments, so tenths would claim precision the measurement doesn't have.
- **Where the range and spread come from**: the headline's source. For a warped track, the warp's segment tempos (marker to marker). Otherwise, crateside's reading windows. See *For investigation* below.
- **Wording**:
  - Note and tooltip: `drifts 110–112 · 2% · from your warp` / `drifts 91–94 · 3% · crateside's reading`.
  - Tools card: `drifts 110–112 · 2%`.
  - Tracks flag: `drifts 2%` (replaces `tempo drifts` for drifting rows; the spread is mono inside the flag). The *Tempo drifts* filter keeps its name.
- **Tempo strip** (2b): on Track details only, for drifting tempos only. It goes under the BPM cell's note, 44px tall, full cell width.
  - One tick per 30-second window (3px bar, `--text-75`), positioned by tempo between the rounded range.
  - A dashed `--text-35` line at the figure shown.
  - Range labels on the left, `0:00` and the track length below (mono 10.5, `--text-40`).
  - Tick tooltip: `2:30–3:00 · ~112`.
  - Its source is the same as the range's. From warp segments, use the duration-weighted tempo of each 30-second window.
  - Monochrome. Not drawn with no reading and no warp.

## For investigation (code agent)

**Short warp segments can spike the drift.** Where the range and spread come from warp markers, a segment between two markers very close together gives an unreliable tempo. A few milliseconds of marker placement error over a short span is a large BPM error, and one such spike sets the range and the spread on its own. Before the formatter uses warp-derived range and spread, please look into a guard, for example:
- dropping or merging segments shorter than some length (a beat count, or a time such as 1–2 s) into their neighbors;
- weighting by duration and using percentiles (say p5–p95) instead of min/max;
- or computing over the same 30-second windows as the reading, so both sources measure alike.

Check it against the user's drifting warps (*Rapper's Delight*, *Hits from the Bong*) and report back which guard you chose and how the ranges moved. The same question may apply to the reading's windows near the start and end of a track. Design has no preference between these options, as long as the range and spread describe the music, not marker noise.

## DESIGN.md changes
1. **New section "Tempo"** after *Type*: the two marks, the state table above, the note grammar, the three source phrases, and "one formatter; summaries via `Bin`/`Range`".
2. **Tracks table**: add the decimal alignment and 84px column; *tempo drifts* now shows on any drifting tempo; the two new flags.
3. **Tools**: "whole-number warped BPM" → the formatted figure. Remove "Re-Pitch is the one place BPMs show two decimals" and replace it with "Re-Pitch figures use the tempo formatter; the clipboard gets the figure without the tilde."
4. **Dashboard**: bins, peaks and ranges are whole with no marks; drill-down rows use the marks.
5. **Drifting** (turn 2): range rounded outward, whole-percent spread, `drifts N%` flag, tempo strip on Track details.
6. **Type**: add "a tilde and weight 400 mark an approximate figure, nowhere else".
