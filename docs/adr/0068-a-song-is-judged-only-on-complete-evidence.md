# 0068 — a song is judged only on complete evidence

- Status: Accepted
- Date: 2026-10-28
- Amends [ADR 0066](0066-keydetect-has-its-own-pinned-build.md) and [ADR 0063](0063-the-backlog-is-judged-in-a-weekly-spend-down.md): when evidence is made whole.

## Context

A song waiting in the library backfill (and a song owed a re-judge) is judged from the `evidence.json` its working set already holds, which an earlier build of crateside wrote with fewer detectors. On 2026-10-28 the backfill judged songs from evidence with librosa, crema and keydetect on an older ffmpeg build only; a minute after each verdict the evidence stage added madmom and edma and re-read keydetect on the pinned build, stamping the run `read_after_verdict` ([ADR 0066](0066-keydetect-has-its-own-pinned-build.md)). The judge weighs a key by how many detectors back it, so those sessions had none of that. New intake was not affected: its evidence is made by the evidence stage on the pinned build, with every detector, before the song is queued for judging. **Measured:** one song's log lines checked; the code path, not each song, was verified.

## Decision

**No song reaches the judge unless its evidence holds a reading from every current detector (keydetect, madmom, edma) and, where the pinned build is known, was read on it.** Three paths are covered: ordinary judging, the spend-down, and judging from bars.

- **The verdict stage completes the evidence for the one song, just before composing the work order** (`EvidenceCompleter`), rather than the selector skipping incomplete songs for the evidence stage to take first. The evidence stage's backfill works on the ledger's judged runs, not on a working set's evidence for an unjudged song, so skipping would have needed a second evidence source there; the verdict stage already prepares a re-judge's evidence at this point.
- **Completing reads all three detectors afresh over the source on disk** (the working set's download, else the kept sample) and replaces those fields in the working-set evidence, the way a re-read does (ADR 0066); every other field stays. It costs about the same CPU time as a re-read.
- **A song whose evidence cannot be completed is not judged.** No source audio, a failing detector, or a detector giving no reading records a failed verdict job, `not judged: <reason>`, and the tick reports it. Failures carry strikes, so after three the song is left alone like any other failing track and the queue moves on; **Retry** on the track clears the strikes. It is never judged on partial evidence.
- A detector that read no key is a reading and does not block; one with no entry at all does.

## Consequences

- A re-judge whose working-set evidence is old is completed too; before this it was judged on that file as it stood.
- Judging from bars completes held whole-file evidence from the audio the user's run uses; evidence it detects itself is already whole.
- Without a known pinned build, only missing detectors count; the build is not checked.
