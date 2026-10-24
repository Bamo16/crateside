# 0064 — The judge's text is plain prose, one job per field, and crateside typesets it

- Status: Accepted
- Date: 2026-10-24
- Changes the verdict file of [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md)
  and how Track details shows it.

## Context

Track details shows the judge's prose as walls of text that repeat findings and mix notation.
Two tracks were looked at on 2026-10-24 (screenshots in
[`docs/design/briefs/20-track-page/`](../design/briefs/20-track-page/)): Paul Oakenfold – *Starry
Eyed Surprise* and Death Grips – *Takyon (Death Yon)*.

What causes it, read from the code:

- **The judge writes two prose fields**, `reasoning` (*Why this key*) and `tonicCheck`. Both argue
  the harmony, so the cadence and the rejected keys come up twice.
- **A window has no reason of its own.** When the judge asks for windows, `VerdictRunner` copies
  the whole withheld pass's `reasoning` into every window's `Why`. *Why the judge asked for these
  windows* is a complete second verdict.
- **Most of the length is about rejected keys**: G♯ minor and B major in the first track; D major,
  F♯ minor, E minor and a D♯ drone in the second.
- **Notation is mixed**: `G#m`, `F#(maj.)`, `bVII`, `E major`. The screen's own `KeyText.Spell`
  shortens to *E maj* on purpose, for tight places.

Measured on the ledger's 547 runs on 2026-10-24:

- **No text contains a newline.** Every reasoning is one paragraph.
- **Markdown is rare but present**: one backtick span and seven `*emphasis*` in `reasoning` and
  `tonicCheck`. No bold, headings, lists or links.
- **ASCII sharps are everywhere**: `[A-G]#` in 325 reasonings and 334 tonic checks.
- `tonicCheck` is set on 397 runs; the rest predate it.

The detector called *edma* on screen is crateside's port of Essentia's
`KeyExtractor(profileType="edma")`. keydetect also has an `edma` profile (`af_keydetect.c`), so
the bare word names two things in a conversation about keydetect.

## Decision

### The verdict file

Each prose field has one job:

| field | job | budget |
|---|---|---|
| `tonicCheck` | what the music does: cadences, loops, the relative and fifth neighbours | ≤ 70 words |
| `reasoning` | what the evidence is worth, and why this confidence; may point to the tonic check, never re-argues it | ≤ 100 words, ≤ 2 paragraphs |
| `candidates` | new: the keys weighed besides the verdict's, as `{ "key", "why" }` | 0–3, one sentence each |
| `windows[].why` | new: what this window should settle | one sentence |

- **A candidate is a key a reader would otherwise ask about**: keydetect's runner-up, a backer's
  dissent, the relative of the chosen key. Zero is allowed. A WITHHELD lists its open keys here
  instead of in `reasoning`. `reasoning` never argues a candidate a second time.
- **The first pass's reasoning is kept once on the run**, not copied into each window, and shown
  once, folded, as *First pass: withheld*.
- **Plain text, no Markdown. A paragraph break is a blank line** (`\n\n` in the JSON string).
- **A chord's function goes after its symbol**: *B (V) → E (I)*, Roman numerals in plain letters.
  This is a wording rule for the judge, because only the judge knows which key a numeral is
  counted from: it often counts against a candidate, not the verdict.
- **The detectors are keydetect, madmom and Essentia.** Essentia replaces *edma* as the name in
  the prompt, on screen and in exported text; the ledger keeps `essentia_edma`.
- **An overrun is logged, not refused.** Resuming a session to shorten it costs a session's
  start-up (about 37k tokens, [ADR 0006](0006-a-verdict-session-costs-turns-times-context.md)).
  The log measures whether the budgets hold.
- **Validation drops a candidate equal to the verdict's key**, and logs it.

### The formatter

Text from the judge is formatted in C# at render time, so old verdicts benefit without being
judged again. It changes only what it recognises with certainty:

- **Inline Markdown markers are removed** (backticks, `*`, `**`, `_`, `__`), keeping the words.
  A permanent guard, since a future judge can ignore the prompt.
- **Paragraphs split on any run of newlines.**
- **Accidentals become ♯ and ♭ only inside a note or chord token.** A `b` in a word is never one.
- **Keys** (a note and a mode word: *major, maj, maj., minor, min*, or a
  mode name) are spelled in full in prose: *E major*, *G♯ minor*, *A Dorian*.
- **Chords** with a quality or an accidental (*G♯m*, *B7*, *F♯maj7*) are recognised anywhere; a
  bare letter only inside a progression, next to `→` or `–`.
- **Roman numerals the judge wrote are typeset** (`bVII` → *♭VII*), never generated.
- **Detector names are made canonical**: *ffmpeg keydetect, KeyDetect* → keydetect; *Essentia
  edma, edma, Edma* → Essentia.
- **Styling is sparse**: keys, chords and r values in Plex Mono; HIGH, MEDIUM, LOW in small caps,
  uncoloured, since colour means state and a word in a sentence is not state. Nothing else.

The formatter also shapes detector data on the same page:

- **A repeated loop in the chord progression is folded**: *G♯m → E ×18 · Em · …*.
- **keydetect's flag** (*DISTRUST (R<0.70: UNRELIABLE BAND)*) becomes a short chip, *r < 0.70*,
  with the band in its tooltip.

### Where each spelling goes

- **Prose uses full forms.** *E major*, *A Dorian*.
- **Tight places keep the short forms**: the verdict band, the Tracks row (*D min* beside *Dor*),
  the sample list. A full mode name beside its collapse would double a narrow column.

### Old verdicts

Rendered as written, through the formatter. A run without `tonicCheck` shows everything under
*Why this key*, without `candidates` shows no *Considered* list. Identical copied window reasons
show once, as *First pass: withheld*. **Nothing in the ledger is rewritten**: a paraphrase of a
music-theory argument cannot be checked at scale, and one that drifts misstates what the judge
reasoned. Verdicts are replaced as songs are judged again.

### Section times

Decided 2026-10-24. The judge gives each section a `start`, in seconds into the track, read from the times of keydetect's key events, which the evidence already carries. That is a time the whole-file evidence has, unlike a cadence time: the judge's times for a cadence would be relative to a window, and the chord stream has none. A section without a start is logged and kept; a negative one is refused. The ledger's `Sections` gain a nullable `Start`, and older sections keep none.

### Not decided here

No timestamped cadence field. The judge's times are relative to a window and the whole-file chord
stream has none, so asking for times would invent precision. Cadences can be read from the chord
lane's own data when it ships with playback (#105).

## Consequences

- The ledger gains the run's first-pass reasoning, each window's own `why`, and the candidates.
- `judge-prompt.md`, `RunValidation`, `docs/judge.md` and `CONTEXT.md` change with the build.
- The share card and *Copy as text* (Track details' share menu) read the same fields, so the
  budgets are what let the card hold the reasoning without cutting it.
