# 0082 - the person's decisions live in the ledger database

- Status: Accepted
- Date: 2026-11-08
- Amends [ADR 0078](0078-the-person-corrects-which-pages-count-for-a-song.md) and
  [ADR 0081](0081-the-person-removes-a-recorded-human-source.md) (where page choices and removed
  sources are kept), [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md)
  (genre's file), [ADR 0071](0071-a-record-kept-under-an-upload-follows-it-to-its-song.md) (the
  per-song files, and the `.bak-` copy it made before moving a record) and
  [ADR 0074](0074-a-data-root-moves-all-of-crateside-state-and-a-demo-library-fills-it.md) (the demo
  library now writes these into its own `ledger.db`).
- Rests on [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md) (the ledger is SQLite,
  migrated by EF Core, backed up on every start).

## Context

What the person decided about their songs was kept in JSON files under `%APPDATA%\crateside\`:
`page-choices.json`, `track-marks.json`, `genres.json`, `familiarity.json`. None is backed up and
none can be rebuilt: a detector rerun gives the same lookup, but not which page the person said was
wrong, which genre they settled, or that a track changes key. `%LOCALAPPDATA%\crateside\ledger.db`
is backed up on every start and before every migration. The ADRs that chose the roaming files said
so because the ledger "records verdicts, one immutable run each"; that is true of `Runs`, and a
separate table beside it keeps it true.

## Decision

The decisions move into `ledger.db`, each in tables of its own beside `Runs`, never inside it.
`Runs` and its children stay immutable; nothing here joins on them.

| table | was | key | columns |
|---|---|---|---|
| `PageChoices` | `page-choices.json` | `Id`; `TrackKey` indexed | url, kind, the page as JSON, made, undone |
| `SourceRemovals` | `page-choices.json` (`removed`, ADR 0081) | `Id`; `TrackKey` indexed | source, made, undone |
| `TrackMarks` | `track-marks.json` | (`Song`, `Mark`) | one row per mark |
| `Genres` | `genres.json` | `Song` | genre, settled, learned from, suggested, heard (JSON list), looked up |
| `Familiarities` | `familiarity.json` | `Song` | artist, fans, rank, looked up, lookup version, settled |

`Song` columns compare without case, as `Runs.Song` does. A page is stored as the JSON the file held,
so a field added to `FetchedPage` needs no migration.

- **Same interfaces.** `PageChoiceStore`, `TrackMarkStore`, `GenreStore` and `FamiliarityStore` keep
  their members and events; they take the `LedgerStore` instead of a path. Each keeps its in-memory
  copy, loaded on first use, and writes the rows it changed through `LedgerStore`, which holds the
  one connection and the one lock the ledger already uses. No new connection style.
- **A write that fails now throws.** The files swallowed an `IOException` and kept the change in
  memory only; a decision that did not reach the database is a bug to see, not to hide.
- **`FollowSongs` (ADR 0071) no longer copies a file aside first.** The start-up backup covers it.
- **The start-up order is backup, migrate, import.** The constructor copies `ledger.db` to
  `ledger-backups\before-<migration>.db` when a migration is pending and then migrates;
  `LedgerStore.Start()` takes the dated backup; `DecisionsImport.Run()` follows it in `Program.cs`,
  before `SongKeyMigration` and before anything reads a store.

### The one-time import

`DecisionsImport` handles each of the four files on its own. If the file exists and its tables are
empty, it adds every record in one transaction and renames the file `<name>.imported-<yyyyMMdd>`
(`-2`, `-3` if that name is taken). It keeps the file; nothing deletes it.

- A file whose tables already hold rows is left where it is, and the start says so in the log.
- A file that cannot be read is left where it is and imports nothing.
- A second start finds no file, so there is nothing to do.
- Records that differ only in case are one record, since the key compares without case.

### What was considered and not moved

| file | verdict | why |
|---|---|---|
| `key-corrections.json` | not moved | ADR 0052 removed the feature; no code reads it and it holds `{}` |
| `names.json` | not moved | names StemForge resolved, or crateside derived from tags: a cache, one metadata fetch per track to rebuild. The names the person typed are in `queue.json` |
| `queue.json` | not moved here; moved by [ADR 0083](0083-track-instructions-playlist-settings-and-intake-live-in-the-ledger-database.md) | its `tracks` are the person's decisions (the stems wanted, a name, an upload to use, "this is not that song", an accepted upload), the largest remaining loss. They share the file with the playlist settings and the work list, and move as their own change |
| `intake.json` | not moved here; moved by ADR 0083 | a record of what was taken on, not a decision; with consume-on-arrival the playlist no longer holds those entries, so it is worth backing up and goes with `queue.json` |
| `warp-settings.json`, `source-retention.json`, `judge-settings.json`, `spend-down.json`, `recipe.json`, `paths.json` | not moved | one value each, chosen in Settings and set again in seconds if lost |
| `paused.json` | not moved | the pause switches and the judge's backoff: state of a run, not a decision about a song |
| `retyped-warps.json`, `written-warps.json`, `transients.json` | not moved | what crateside did to sidecars, re-derived by reading them |
| `rejudge.json` | not moved | the songs crateside decided are owed a judgment |
| `builds.json` | not moved | the filter builds, seeded on a first start |
| `actions.jsonl`, `deletions.jsonl`, `jobs.jsonl`, `stage-notes.jsonl` | not moved | logs of events |
| `beats.json`, `tempos.json`, `loudness.json` | not moved | caches |

`genres.json` and `familiarity.json` hold the lookup's results beside the person's answer; they move
whole, one row per song, because the lookup's `LookedUpAt` is what stops the workers asking again.

## Consequences

- A restored ledger backup restores the decisions with the verdicts. This reverses the consequence
  ADR 0078 and ADR 0081 accepted ("a restored ledger backup does not restore it").
- `ledger.jsonl` still exports runs and restatements only; the decisions are not in it.
- The files crateside no longer reads stay named in `CratesidePaths`, for the import alone.
- Tests build their stores over an in-memory `LedgerStore`; a new store over the same ledger is a
  restart.
