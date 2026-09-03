# 0008 — "staged since" is the file's creation time in `Sample Staging`

- Status: Accepted
- Date: 2026-09-03

## Context

The *In staging* view replaces the *Next* column with **Since** — "staged 3 weeks ago". It is the
only thing on that view about time, and it earns its place because a track rests in staging for
weeks: `Sample Staging` is not a queue and implies no urgency, so *how long* is most of what there
is to say about a track sitting in it.

**Nothing recorded it.** The handoff stage moves files and writes no timestamp, and the staging
board holds no state of its own by design. The implementation notes for this work named the choice
and left it open: record a handoff timestamp, or read the file's creation time, "pick one and write
down which".

## Decision

**Read the creation time of the track's files in `Sample Staging`, and take the earliest.**

Earliest because handoff moves a track's files together, so the oldest of them is when the track
landed. Files that have reached `Mashup Samples` are excluded — they are not staged any more, and a
track with none left is not staged since anything.

**Creation time, never last-write time.** These are different facts and the difference is not
subtle here, because an **instrumental release** keeps its source track: that file was downloaded
once and then carried through separation unmodified, so its last-write time is when it was
acquired and has nothing to do with staging. Measured on 2026-09-03, in the live folder:

| file | created (arrival) | last written |
|---|---|---|
| `Groove Armada - At the River.flac` | 09-02 16:09 | **08-17 16:00** |
| `Snail's House - Grape Soda.flac` | 09-02 16:11 | **08-17 16:01** |
| `Starjunk 95 - 99th Street.flac` | 09-03 13:06 | **09-02 19:04** |

Last-write would have called the first of those "staged 17 days ago" the day after it arrived —
wrong, and wrong in the direction that makes work look more stale than it is.

## Why not record a handoff timestamp

It is the more obviously correct answer and it was rejected on one ground: **it would be null for
everything already staged.** The tracks the column exists to describe are the ones that have been
sitting there longest, and those are precisely the ones that predate any field crateside starts
writing now. A column that reads blank for the oldest rows fails at its only job until the library
has cycled completely through.

It also adds the first piece of stored state to a view that is otherwise entirely derived.
`Sample Staging` is self-cleaning — things leave rather than being marked done — and a recorded
timestamp would need reconciling against a folder that changes without crateside watching.
[ADR 0002](0002-there-is-no-warped-signal.md) made the same call for the same reason when it
refused a hand-marked "warped" flag.

## What is measured and what is not

**Measured:** that creation time marks *arrival in the folder* rather than creation of the
content. Three samples moved by the user in Ableton's browser on 2026-09-02 landed in
`Mashup Samples` with creation times *later* than their last-write times, which means a move
between these roots copies and deletes rather than renaming. Handoff shows the same shape: files
appear in staging a couple of minutes apart per track, in the order the stage wrote them, with
source tracks carrying much older last-write times.

**Not measured:** the long tail. Nothing in `Sample Staging` is currently older than a day — the
library was worked through on 2026-09-02 and 09-03 — so "staged 3 weeks ago" has not been seen
against real data. The mechanism is validated; the case the column was designed for is not, and
will not be until something sits there long enough.

**Known to be wrong, accepted:** a file re-copied into staging by hand reads as newly arrived.
That is the same trade as everywhere else here — the filesystem is the record, and an operation
crateside did not see is one it cannot account for.

## Consequences

- `StemOnDisk` carries `CreatedAt`, read during the scan that already opens every file. One
  `stat` beside a TagLib open costs nothing measurable.
- **Null where the filesystem will not say.** Windows reports 1601-01-01 when it has no answer,
  and "staged 425 years ago" is worse than an absent value, so the reader returns null instead.
- A track whose files have all moved on has no `StagedSince`, which is correct: it is done.
