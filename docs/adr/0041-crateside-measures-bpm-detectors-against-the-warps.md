# 0041 — crateside measures the BPM detectors against the warps, and shows it on Counts

- Status: Accepted; the Dashboard panel was replaced by [ADR 0075](0075-the-dashboard-shows-how-the-starter-warps-fared.md)
- Date: 2026-09-27
- Answers part of [#3](https://github.com/Bamo16/crateside/issues/3). The corpus and key-detector
  parts stay open (below).

## Context

#3 asked which repo computes detector and corpus statistics, and whether crateside displays them.
It was written when the only ground truth was a human source recorded in keytag's ledger, which
keytag already reads.

For BPM that changed with [ADR 0038](0038-a-warps-bpm-is-the-stable-tempo-that-holds-longest.md): a
finished warp's tempo, set by ear, is the true BPM (the user's statement, 2026-09-26). It lives in
Ableton's `.asd` sidecars, which only crateside reads. keytag records what its detectors said
(`bpm.value`, keytag's recommendation, and `bpm.beatdetect`, ffmpeg's raw figure) and has no access
to the warps.

Detector accuracy was asked for on the page that became Counts, mainly for ffmpeg's
`beatdetect`, whose maintainer they can take it to (#68, 2026-09-26).

## Decision

- **crateside computes BPM detector accuracy**, joining each library song's warped BPM to its latest
  ledger run. It reads `runs.jsonl` and writes nothing to it, so keytag's ledger stays the declared
  interface (keytag ADR 0006) and nothing new crosses between the repos.
- **Counts shows it** as *Detectors against the warps*: five bands per detector (exact, close, half
  or double, triplet or dotted, wrong), each band's songs with a *Copy as text* list for the filter's
  maintainer, and where the misses land. The rules are in `DetectorBands` and handoff 08.
- **Nothing is written to git** for it. The numbers are recomputed on each read, so the
  markdown-file friction #3 describes does not arise for this view.

## Not decided here

- **Key detector success rates.** Their ground truth is still a human source in the ledger, which
  keytag reads as well as crateside does. #3's reasoning (the audience works in keytag, with a
  terminal) still applies to them.
- **Corpus candidacy** for keydetect-harness, and the BPM corpus for `beatdetect` (#68, step 4).

## Consequences

- The view depends on the ledger's `bpm` block keeping its shape; `LedgerRun` reads both figures.
- Measured on the first build (2026-09-27, 351 songs with a warp and a run): keytag's
  recommendation exact on 223, `beatdetect` on 138; `beatdetect` read 114 at a triplet or dotted
  multiple.
