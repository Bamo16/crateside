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
