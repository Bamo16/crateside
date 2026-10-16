# 0055 — crateside owns the key judge and its ledger

- Status: Accepted
- Date: 2026-10-15

## Context

crateside reaches keytag, a sibling checkout, in four ways: `keytag-detect` for the evidence
(Python: librosa, CREMA in its own TensorFlow venv, and a subprocess calling ffmpeg's
`keydetect`), the `/detect-key` skill with its working directory in the checkout for the verdict,
`keytag-restate` (shelled out from four places) to say an upload is a song it already has, and
`results/runs.jsonl` itself, read at more than fifteen call sites. A stranger cannot be asked to
clone keytag beside crateside ([#107](https://github.com/Bamo16/crateside/issues/107), make
crateside public), and the skill is tuned to one provider's tools: its `curl` recipes have never
run in crateside's session, whose Bash allows only `uv run`.

What was measured is in three notes: the judge on a non-Claude model
[without search](../notes/2026-10-15-a-non-claude-judge-on-ten-judged-tracks.md) and
[with it](../notes/2026-10-15-the-non-claude-judge-with-web-search.md), and
[the detectors and sources scored across the ledger](../notes/2026-10-15-the-detectors-and-sources-scored-across-the-ledger.md).
The shape was proposed in [#108](https://github.com/Bamo16/crateside/issues/108) (keytag's future)
and settled with the maintainer.

## Decision

**The judge's only job is a key verdict.** No BPM (keytag ADR 0010 already records none);
beatdetect is the tempo source.

**Detectors.**
- All three of keytag's detectors earn their keep. Alone, CREMA and librosa trail keydetect, but
  whether they back keydetect's answer predicts its being right far better than its own `r` does.
- Before anything is ported, the detectors are **chosen by a bake-off**: Essentia's
  KeyExtractor (with its `edma` profile), madmom's CNN key model and libKeyFinder, against
  keytag's three. Each is scored alone, as keydetect's backer, and as a set, over
  keydetect-harness's corpus and the ledger's human-sourced songs.
- The chosen set is then **ported to .NET inside crateside**. A detector that cannot run in .NET
  is run through ONNX or left out, decided from the bake-off's figures in an addendum here.
- Until the port lands, the evidence stage keeps calling `keytag-detect`.

**The ledger.**
- It moves into **SQLite in crateside's data folder**, configurable and not in git. Every user of
  crateside has one; its format is documented in this repo.
- keytag's history is imported once, restatements and the old BPM estimates included. Those
  estimates stay readable as an old estimate and are never written again.
- crateside takes a dated backup with SQLite's backup API at each start and keeps the last seven.
  A JSONL export exists for reading by eye.

**A verdict records what the music is, not its tag.**
- It holds a **tonal centre**: a tonic and one of the seven diatonic modes.
- Optionally it holds **sections**, each with a label, tonic and mode, for a song that moves.
- It can be marked **mixed**, for two modes on one tonic.
- The 24-key form Ableton is tagged with is computed by the **collapse**, keeping the tonic
  (A Dorian → A minor), and is never stored as the truth. A song with sections collapses from the
  centre the judge names as primary; a mixed one from its harmony's mode, not its melody's.
- Imported rows become tonic plus Ionian or Aeolian, or the mode they stated.
- Wherever a key is shown and the verdict is not plain major or minor, the mode is written out
  beside its tag ("A Dorian → A minor"), with the same-notes neighbour one Camelot step away as a
  hint. The page design goes to Claude Design.

**The session writes a verdict file and nothing else.**
- crateside names the path. After the session it validates the file and stores it; the session
  has no recorder, no uv and no git.
- A session that ends without a valid file is resumed once (`--resume`, "write the verdict now",
  with the validation error if there was one) before the failure counts as a strike.
- Saying an upload is a song already judged becomes a crateside ledger operation, replacing the
  four `keytag-restate` calls.

**The runner stays `claude -p`.**
- The judge's endpoint is a setting: environment passed to the judge process, documented with an
  official Anthropic-compatible API as the example. A backend is trusted only after its own
  agreement check against ground truth. Nothing specific to an unofficial bridge goes in the repo.
- The session's tools are `Read`, `Write`, `WebSearch` and `WebFetch`. Without Bash, the judge's
  `--segment` re-run goes (4 of 529 machine layers used one).

**The instructions are a plain prompt file crateside owns**, passed with
`--append-system-prompt`, not a skill. It is rewritten from scratch around keydetect's figures,
when to search, the trusted sites and the verdict schema, to about sixty lines. A variant for one
model is added only when that model is measured to need it. Two rules are fixed in it:
- An authoritative source that states another key **caps the verdict at MEDIUM**, whatever the
  chord argument. Six HIGH verdicts overruled one.
- Algorithmic key sites (Tunebat, SongBPM and the like) are not sources. They matched the verdict
  less often than keydetect alone.

**crateside fetches corroboration before the session**, from Hooktheory, Musicnotes and Ultimate
Guitar by artist and title, and puts what it finds in the evidence with the sections each page
covers. The judge decides whether a page is the right recording and may still search. Its effect
on turns, tokens and wrong-song pages is measured on Claude.

**keytag stays as a repo** for `phase-align` and as history. Its ledger and detect-key parts are
frozen with a pointer here once crateside no longer reads them. The shared glossary terms
(Verdict, Song identity, Source video ID and the rest) move into crateside's `CONTEXT.md`.

## Order

The bake-off comes first, since what is ported depends on it. Then: the ledger with the new
verdict shape, the judge rewrite (prompt, verdict file, resume, endpoint), pre-fetching, the
detector port, the display, and finally retiring every reference to keytag.

## Addendum, 2026-10-15: windows replace the judge's `--segment`

Dropping the `--segment` re-run (above) lost the one method that rescued tracks nothing else did:
Houshou Marine's *A Horny Money World*, withheld on a three-way split until the maintainer gave
the choruses as bars 33–48 and 82–96 from their warp, and two songs where the agent chose a
window itself (Chris Rinox – Burst Mode, Death Grips – Takyon). Windows stay; crateside runs
them, not the session.

- **The maintainer can give windows** on a track's page: bar ranges with an optional label
  ("Chorus: 33–48, 82–96") and an optional note to the judge. Bars are converted to seconds
  through the warp of the sample Ableton shows, so the numbers match what the maintainer reads
  there; with no warp they are given in seconds. crateside detects over each window and judges
  again with that evidence. The windows become the verdict's sections, and the direction is
  recorded with the run.
- **The judge can ask for windows**: its verdict file may say WITHHELD with the windows to detect
  over. crateside runs them unattended and resumes the same session once with the new evidence.
- **The maintainer's ear is a hint**, recorded as a source of its own kind, which the judge
  weighs but which never raises confidence by itself.
- Every ported detector accepts a time window.

## Addendum, 2026-10-15: how much the user's ear counts is a setting

Users will trust their own ear differently; the maintainer does not trust theirs yet. **Ear** is a
setting with two values: **hint** (the default, as above: weighed, never raising confidence) and
**human source** (counted like an authoritative source, so a dissent from it caps the verdict
at MEDIUM and agreement with a backed detector can reach HIGH). The judge is told which applies.

Asking for windows is given to the judge as authority, not a last resort: where the evidence
looks odd (a three-way split, a detector failing, a long stretch with no harmony), it should ask
for windows before withholding.

## Addendum, 2026-10-15: the ledger is EF Core over SQLite, and the import follows the file

Built in [#110](https://github.com/Bamo16/crateside/issues/110); the format is in
[docs/ledger.md](../ledger.md).

- **The schema is EF Core's**, changed only by migrations applied at start, at the
  maintainer's request; it suits a schema that grows again with the judge rewrite, windows
  and the Ear setting.
  One connection is held for the store's life and handed to EF, which is what lets SQLite's
  backup API copy it and an in-memory ledger serve the tests.
- **The import repeats whenever keytag's file changes**, not only at start as #110 said. Until
  the judge rewrite, a verdict session ends with keytag's recorder appending a line, and the
  verdict stage reads the ledger straight after to decide whether the session succeeded; an
  import at start alone would count every such session a failure.
- **A keytag line is identified by its timestamp**, as keytag's own restatements point at
  it. A second line with the same timestamp is refused, not merged.
- **The import refuses what the ledger would refuse**: a line with no confidence, or one
  outside the four, is listed and left out rather than stored as an unreadable verdict. On
  2026-10-15 the real history imported with no refusals.

## Addendum, 2026-10-15: the set to port is keydetect, madmom's CNN and Essentia's `edma`

The bake-off ([note](../notes/2026-10-15-the-detector-bake-off.md), #109) scored Essentia,
madmom and libKeyFinder against keytag's three on the harness corpus (37 tracks) and the ledger's
221 human-sourced songs with audio. This replaces the first bullet of **Detectors** above: CREMA and
librosa do not earn their keep once madmom and Essentia are on the table.

**Ported (#113):**

| detector | route in .NET | why |
|---|---|---|
| ffmpeg `keydetect` | none: the ffmpeg call crateside already makes | best alone on the corpus (81%), level with the rest on the ledger (71%); its `r`, chords and key flicker feed the judge |
| madmom CNN key model | ONNX Runtime; the weights converted from `key_cnn.pkl`, the log-spectrogram front end written in C# | best backer on both yardsticks (AUC 0.82 on the ledger against CREMA's 0.71), and right on 36 of the 64 songs keydetect gets wrong |
| Essentia `edma` | native C#: HPCP and the profile correlation | the best Essentia profile (73% alone, AUC 0.75); about 0.25 s a track; no model |

With keydetect, the pair madmom + `edma` backs its answer best on the ledger (AUC 0.844 against 0.751
for CREMA + librosa; where both back it keydetect is right 92%, where neither does 21%). It ties with
CREMA + madmom (0.843); `edma` is chosen over CREMA because it needs no TensorFlow, no venv and no model.

**Left out:** CREMA, librosa K-S, libKeyFinder (64% alone; GPL; a native build with FFTW) and
Essentia's other profiles (`bgate` and `shaath` trail `edma` and add nothing to the set; `temperley`
and `edmm` miss mostly by the relative key).

**The evidence** keeps the shape the judge reads. keydetect's chord stream and flicker stay; each
ported detector contributes its key and a confidence (madmom's probability, Essentia's strength); and
"backed by both / one / neither" counts madmom and `edma` against keydetect's answer. The judge's
prompt takes its figures from the note's backed-by table, rewritten for these two partners.

**Windows:** all three take a window by decoding only that range (checked in the note). No port
needs its own windowing.

**Acceptance for #113:** each port gives the same key as its Python original on every track in
[the readings CSV](../notes/2026-10-15-the-detector-bake-off.csv) (`madmom`, `ess_edma` columns), or
each difference is explained in the commit.

**Licences, for #107.**
- madmom's model files are CC BY-NC-SA 4.0, and a converted ONNX file is derived from them. The
  ONNX file is committed beside the code that loads it with its own licence file and a line in the
  README saying crateside's use of it is non-commercial. Choosing crateside's own licence has to
  account for that.
- Essentia is AGPL-3.0. The `edma` port is written from the published algorithm (Gómez's HPCP;
  Faraldo et al.'s profiles, taken as data) and the parity check above, not by transcribing Essentia's
  source. If parity cannot be reached without copying code, the maintainer decides before it merges.

## Addendum, 2026-10-16: the same-notes key is not always one Camelot step away

The first addendum's hint ("the same-notes neighbour one Camelot step away") holds only for Aeolian,
Lydian and Mixolydian. Worked for A: Dorian shares G major's notes (tag 8A, notes 9B), Phrygian F
major's (8A, 7B) and Locrian B♭ major's (8A, 6B), none of them adjacent on the wheel. The hint names
the same-notes key and its Camelot code; it does not claim a distance. Brief 17 carries the table.

## Addendum, 2026-10-16: the judge writes a verdict file, and keytag's history is imported once

Built in [#111](https://github.com/Bamo16/crateside/issues/111) (the judge rewrite); how it runs
is in [docs/judge.md](../judge.md).

- **The import no longer repeats.** The third addendum's repeat import existed only because
  keytag's recorder was still writing verdicts. The judge now records straight into the ledger,
  so keytag's file is imported once, into a ledger that holds no run yet, and never read again
  after that. `Paths:KeytagLedger` stays readable for retiring keytag (#115).
- **crateside keeps the song identity.** The verdict file names artist and title; crateside
  builds the identity from them as keytag's recorder did, except that a track already in the
  ledger keeps the identity it has, so a re-judged song supersedes its old run whatever the
  judge calls it.
- **Windows asked for and a missing file are resumed separately**, each at most once per run. A
  WITHHELD verdict that asked for windows is recorded as it was if the resume writes nothing
  usable.
