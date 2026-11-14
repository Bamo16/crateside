# 0065 — the starter warp's beats are read on the source

- Status: Accepted
- Date: 2026-10-24
- Amends [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md): which file beatdetect reads.

## Context

beatdetect read the upload's instrumental, else its kept source, else its karaoke. The source (the
full mix from `source-index.json`, in the working set or kept as a sample) is what a detector is
meant to be measured on, and 507 of 511 library uploads have one on disk.

**Measured 2026-10-24, read-only, on 30 tracks with the maintainer's saved warps:** each source is
sample-aligned with its stems (identical sample counts, cross-correlation lag 0 on all 30), so a
reading on the source applies to the stems' timeline unchanged. Both files read every BPM within
0.01. Beat offset against the saved warp: median 7.2 ms on the source against 8.2 ms on the
instrumental, p90 22 against 36 ms. Two octave errors on the instrumental read right on the source;
one track was slightly worse on the source.

## Decision

**An upload's beats are read on its source when crateside holds one, else on the instrumental it
would have used before** (kept source, then karaoke). The transients, the marker and every stem's
timing still come from the reference *stem*; only the beatdetect reading moves.

- Readings stay keyed by file path and the file's length and write time, so the source is a new key:
  it is read once, and the instrumental's old reading is left alone. A source gone to retention makes
  the instrumental the reference again, and its own reading, re-read if from an older build, serves.
- Until the source is read, the stem's own reading is used, so a starter already written is not
  replaced by Warp off while the worker catches up.
- The beat worker queues one file per upload, never both.
