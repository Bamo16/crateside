# crateside: Track details, around the judge's text, a key strip and sharing

Issue: #150

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Warp view*, *Side stack*, *Sample list*, and *Track details* under
*Differs from the design*). The page's canvases are *Crateside Track Page*, *Crateside Warp View*
and *Crateside Judge From These Bars* in `docs/design/canvases/`. The text rules this design
serves are [ADR 0064](../../adr/0064-the-judges-text-is-plain-prose-and-crateside-typesets-it.md);
terms (verdict, tonic check, candidate, window, section, backer, key flicker) are in `CONTEXT.md`.

Screenshots of today's page are in [`20-track-page/`](20-track-page/), two tracks:

- Paul Oakenfold – *Starry Eyed Surprise*: `starry-eyed-1-top.png`, `starry-eyed-2-evidence.png`.
  A plain MEDIUM verdict; note the 70-chord progression.
- Death Grips – *Takyon (Death Yon)*: `takyon-1-top.png` to `takyon-4-whole-file.png`. A verdict
  reached through three judge's windows; note how the right column grows when they and the folds
  open, and the 27-row key flicker.
- `tracks-row-short-forms.png`: the Tracks row's key column, *D min* beside *Dor*, which is why
  tight places keep short key names.

---

## Where it stands

Track details' lower half is the evidence panel: *Why this key* and *Human sources* on the left;
*What the detectors reported*, *Tonic check*, *Chord progression*, *Key flicker* and *Tempo* on the
right, with *Windows* above them when the run had windows. Its problems:

- **The judge's text is walls of prose** that repeat findings and mix notation. That is fixed in
  the text itself (below), not by the design, but the design decides how the fixed text sits.
- **The columns unbalance.** Windows, each with a three-detector table, plus the *whole file* and
  *key flicker* folds all stack in the right column; the left column ends half-way down.
- **keydetect's flag wraps badly**: *DISTRUST (R<0.70: UNRELIABLE BAND)* breaks across two lines
  in the detector row and pushes its runner-up under it.
- **Key flicker is a list of running-estimate changes**, many under a second apart. It is evidence
  for the judge. A person only needs to see a real key change.
- **The page cannot be shared.** The maintainer sends verdicts to friends (Discord, text) and to
  agents, by screenshot and copying.

## What changes in the text (decided, ADR 0064)

Design for text of this shape; old verdicts keep their longer prose and must still sit well.

- **Why this key** (`reasoning`): what the evidence is worth and why this confidence. At most 100
  words in at most 2 paragraphs. Old verdicts are one paragraph of up to ~370 words.
- **Tonic check**: what the music does. At most 70 words. Old runs may have none; then everything
  is under *Why this key*.
- **Considered** (new): 0–3 keys the judge weighed besides the verdict's, each a key and one
  sentence (*G♯ minor — the relative; nothing resolves to it*). For a WITHHELD, the keys still open.
- **A window's why** (new): one sentence per judge's window, saying what it should settle.
- **First pass: withheld**: the reasoning of the pass that asked for windows, shown once, folded.
  Today it is repeated as *Why the judge asked for these windows*.
- **Typesetting**: keys, chords and r values in Plex Mono (*G♯ minor*, *B (V) → E (I)*, *r 0.837*);
  HIGH/MEDIUM/LOW in small caps, uncoloured. Nothing else is styled. Keys are spelled in full in
  prose and short in tight places (the verdict band, the Tracks row, the sample list).
- **The detectors are keydetect, madmom and Essentia** (today *ffmpeg keydetect* and *Essentia
  edma*).
- **keydetect's flag becomes a short chip**, *r < 0.70*, with the band in its tooltip.
- **The chord progression folds its loops**: *G♯m → E ×18 · Em · G♯m → E ×12 · …*.

## What the design has to cover

### 1 · The evidence panel, balanced

The direction, decided; the layout is yours, and say so if it reads worse than an alternative:

- **Windows leave the right column for the timeline.** The Warp view already draws each window as
  a band in every row (judge's dashed, yours solid). A window's readings, its label, its one-line
  why and its section (if the verdict gave it one) open from its band, under the timeline or
  beside it. Today's window cards go.
- **Left**: *Why this key* (paragraphs), *Considered*, *Human sources*, and *First pass: withheld*
  folded where there was one.
- **Right**: the whole-file detectors, *Tonic check*, the folded progression, *Tempo*.
- **The detector row** (name · key · r · chip · runner-up) on one line at the panel's width, in
  the whole-file table and in a window's readings.

States to show: a plain verdict (*Starry Eyed Surprise*), a verdict through judge's windows
(*Takyon*), an old verdict with one long paragraph and no tonic check, a WITHHELD with three
candidates.

### 2 · The key strip

A lane under the waveform lanes, on the Warp view's time axis, replacing the *Key flicker* fold:

- **The verdict's sections as bands**, each labelled with its key (short form). One band across the
  track when the verdict has a single centre. This is the "real key change" a person reads.
- **The raw running-estimate changes as a faint underlay**, shaded by r, for the judge's eye and
  anyone curious. It must not compete with the bands.
- It follows the zoom pills like the other rows.

### 3 · Room for what comes next

Not drawn in detail, but the layout must not need redoing for them:

- **A chord lane** under the key strip: one detector's chords over time (which detector is chosen
  later). It ships with audio playback (#105, audio playback and warp editing).
- **Playback**: a transport and a playhead across every row. Placing the transport is yours; it
  should not take the place of the zoom pills.

### 4 · The share menu and its card

A share control on Track details with three actions: **Copy image**, **Save image**, **Copy as
text**. Placing it is yours: it shares the verdict, so it belongs near the verdict band or the
evidence panel, not in the side stack's file actions.

**The card** is its own layout, about 1200 px wide, not a crop of the page. It is what a friend
sees in Discord, so it reads at preview size without the app around it:

- `Artist – Title`
- key, confidence and BPM, as the verdict band shows them
- the subtitle line (*No human sources; 3 detectors and the chord check*)
- the three detectors' readings
- *Tonic check*, *Why this key* and *Considered*
- a small crateside mark

With the new budgets it fits without cutting; an old long reasoning is cut at a paragraph with *…*.
Show it for *Starry Eyed Surprise* and for a verdict with sections. It may use the app's dark
palette or a lighter one of its own; say which and why.

*Copy as text* needs no design: it puts Markdown on the clipboard.

## Not in scope

- The upper half of Track details (verdict band, stage flow, sample list, side stack), except
  where the share control sits.
- The chord lane's content and playback's behaviour; only their room.
- The text rules themselves: ADR 0064 settles them. Comment on #150 if one makes the layout worse.
