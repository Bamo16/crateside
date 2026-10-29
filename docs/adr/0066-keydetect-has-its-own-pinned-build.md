# 0066 — keydetect has its own pinned build

- Status: Accepted
- Date: 2026-10-24
- Extends #102 (the build picker) from beatdetect to keydetect.

## Context

keydetect ran on `Paths:KeydetectFfmpeg`, `PATH`'s `ffmpeg` by default: a NoMercy build from before
keydetect reported a version. keydetect-harness report 07 (commit `0e49e72` there) adopted
keydetect 1.2.1: chords 69 to 71.5% on the corpus, sevenths 61 to 69%, key accuracy neutral, and
the confidence bands at r 0.70 and 0.75 still hold. Those numbers are the harness's, measured there.

beatdetect already ran on a pinned build (`builds.json`, identified by sha256). Two facts about the
1.2.1 build shaped what follows, **measured 2026-10-24**:

- It has no beatdetect filter, so one build cannot be pinned for both filters.
- It is a minimal build: its only muxers are `null` and `wav`, so it cannot decode to the raw
  `f32le` that madmom and edma read. A trial re-read through it failed both with "Error opening
  output files".

A judged song's keydetect reading lives only in its latest run's evidence in the ledger, which
records the ffmpeg's version line and sha256. Re-judging a song puts that stored evidence back
in front of the judge when the working set no longer holds it.

## Decision

**Each filter has its own known builds and its own pin, in the one `builds.json`.** The file keeps
a section per filter; a file from before this (beatdetect's builds at its top level) loads as
beatdetect's, pin included. A filter that knows no build is seeded at start-up from its configured
executable (`Paths:BeatdetectFfmpeg`, `Paths:KeydetectFfmpeg`), a name on `PATH` included, and that
first build is pinned. The pin is read at start-up, so a pin changed by editing `builds.json` takes
effect on the next start.

**keydetect runs on its pin; madmom and edma decode through `Paths:KeydetectFfmpeg`**, because a
keydetect build need not be able to decode for them. The evidence's `versions.ffmpeg` and
`ffmpeg_sha256` name the keydetect build.

**Evidence that another keydetect build read is read again, behind fresh work.** When the evidence
stage has nothing queued, after any judged song missing a reading has had it added (#139), it takes
one judged song a tick whose latest evidence records a different `ffmpeg_sha256` from the build
keydetect runs on, or none, and reads all three detectors afresh over its source on disk. keydetect,
madmom and edma, the backers, the pairwise agreement, the chords, the key changes, the input and the
versions are replaced; every other field stays. All three are read again, not keydetect alone,
because the backers and the agreement are computed against keydetect's key. A re-read in which any
of the three reads no key changes nothing and is not retried until the app restarts.

- **Any change of build re-reads, not only a newer one** as beatdetect does: the evidence is what a
  re-judge is shown, so it should come from the build the bands were checked on.
- **Nothing is re-judged.** The run, its verdict, its time and its judging are untouched, and
  nothing in crateside queues a re-judge on its own. A verdict whose keydetect reading has now moved
  shows it beside the verdict until the maintainer re-judges it. Report 07 counted 22 ledger verdicts
  that would move.
- The earlier readings are not kept in the evidence. The ledger backup taken at each start holds
  them, for the seven starts that backups are kept.
- A working set's own `evidence.json` is not rewritten, and a re-judge prefers it over the ledger's.
  On 2026-10-24, 59 working-set folders held one.

## Consequences

On this machine's first start with this change, 488 of the 519 judged songs with evidence have a
source on disk and will be re-read, about 8 seconds each on the CPU (one trial, measured), one per
idle evidence tick. The other 31 stay as they are until a source is downloaded.

## Amendment, 2026-10-24: a late reading says so

A re-read, or a backfill into a finished run, adds `read_after_verdict: {"at": …}` to the evidence
(`at` null where only derived), and Track details says *Read on keydetect 1.2.1, after this verdict.*
under the detector table, since the judge's prose can quote a reading the table no longer shows. The
first start after this stamps, once, each latest run whose evidence carries the pinned build's
`ffmpeg_sha256` but was recorded before 17:21 on 2026-10-24, when the re-reads began.

## Amendment, 2026-10-28: a new verdict carries no marker

A re-judge starts from its song's latest evidence, and that evidence carried the earlier run's
marker into the new run, so the table said the judge had not seen readings it had. **MEASURED
2026-10-28:** 67 runs held a marker older than the run itself (63 of the 66 re-judges of that day,
ledger Ids 580–645, and four earlier re-judges). crateside now drops the marker when it writes a
re-judge's evidence and again when it records any verdict, and the `InheritedLateReading` migration
removed it from those runs: each marker older than its run, and each without a time on a run
recorded after the re-reads began.
