# 0083 - track instructions, playlist settings and intake live in the ledger database

- Status: Accepted
- Date: 2026-11-08
- Completes [ADR 0082](0082-the-persons-decisions-live-in-the-ledger-database.md), which left
  `queue.json` and `intake.json` for this change. Amends
  [ADR 0021](0021-the-playlist-is-a-message-queue-consumed-on-arrival.md) (intake's file).

## Context

ADR 0082 moved the person's decisions about songs into `ledger.db`, which is backed up on every
start. `queue.json` was left behind: its `tracks` list holds what the person told crateside about
particular tracks, and nothing can rebuild it. `intake.json` was left because it records what was
taken on rather than a decision. Both were checked against the real files on 2026-11-08.

## Decision

Both files move, in the same pattern as ADR 0082: tables beside `Runs`, the stores keep their
members and take the `LedgerStore`, a write that fails throws, and `DecisionsImport` moves each file
once into an empty table and renames it `<name>.imported-<yyyyMMdd>`.

| table | was | key | holds |
|---|---|---|---|
| `TrackInstructions` | `queue.json` `tracks` | `TrackKey` (video IDs are case-sensitive, so no `NOCASE`) | the stem plan as JSON (the stems wanted, added sources, adopted samples), typed name, upload override, "not that song", accepted upload, re-cut request, added-by-URL and library-backfill flags |
| `PlaylistSettings` | `queue.json` settings | one row, `Id = 1` | playlist ID, refresh minutes, consume-on-arrival |
| `Intake` | `intake.json` | `Id` in arrival order; `TrackKey` unique | one row per track taken on: title, channel, added-at, premium outlook, unavailable flag, page URL, artist, song title, release as JSON, and the removal bookkeeping (attempted-at, failure reason and whether a retry can help, removed entry ID) |

### The playlist settings moved too

They could have stayed in a slimmer `queue.json`, as the other one-value settings stay in their
files. They moved because the import renames the file it reads: a settings file left behind would
have to be written out again by the import, and it would be the one file in `%APPDATA%` that the
import both reads and keeps. Consume-on-arrival also removes entries from the person's playlist for
good, so losing it silently to "off", or the playlist ID to the configured seed, is worse than losing
a tolerance in Settings. A row is imported only when the file had saved a setting, so configuration
stays in charge otherwise.

### Intake is backup-worthy

On the real file (467 records, 2026-11-08): 359 carry an `addedAt` and 126 a channel, which the
consumed playlist no longer holds; 36 are file imports whose `artist`, `songTitle` and `release`
came from the file's tags and are in no other store; 238 record that the entry was offered back.
Consume-on-arrival is on, so for most of these intake is the only copy.

### Reshaped

- `QueueFile` and its JSON context are gone; the stores take the `LedgerStore`. The cache that
  noticed an edit made outside the process by the file's time stamp goes with it.
- `IntakeStore.IsDurable` is gone. It existed because a failed write was swallowed and
  `PlaylistConsumer` must not remove entries on the strength of records that were not saved. A
  failed write now throws, so the scan stops before it removes anything.
- `TrackInstruction.WasInstrumentalRelease`, the pre-`StemPlan` flag, is read only by the import
  (`LegacyTrackInstruction`), which converts it.

### The import

- An instruction that says nothing (`SaysSomething` false) is not imported; the store drops such an
  entry on its next change anyway.
- A key that repeats keeps its first record.
- `queue.json` fills `TrackInstructions` and `PlaylistSettings` together, and is left alone when
  either holds rows.

## Consequences

- A restored ledger backup restores the instructions, the playlist settings and the intake with the
  verdicts.
- `CratesidePaths.QueueFile` and `IntakeFile` remain, for the import alone.
- Nothing in `%APPDATA%\crateside` holds a decision about a track any longer. What remains is
  listed in ADR 0082's table of files not moved.
