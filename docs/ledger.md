# The ledger

crateside's record of every key verdict: what the judge concluded about a song, and the
evidence behind it. A SQLite database, read and written only through `LedgerStore`
(`src/Crateside.Core/Ledger/`). Decided in
[ADR 0055](adr/0055-crateside-owns-the-key-judge-and-its-ledger.md).

## Where it lives

| what | where |
|---|---|
| the database | `%LOCALAPPDATA%\crateside\ledger.db`, set by `Paths:LedgerDatabase` |
| a backup per start, the last seven kept | `ledger-backups\ledger-<yyyyMMdd-HHmmss>.db` beside it |
| a copy before a migration applies, never pruned | `ledger-backups\before-<migration>.db` beside it |
| the export, for reading by eye | `ledger.jsonl` beside it, rewritten after every change |

None of it is in git. A backup is a whole database (SQLite's backup API), so restoring one is
copying it over `ledger.db` while crateside is stopped.

## The tables

The schema is EF Core's, changed only by a migration in `Ledger/Migrations/` and applied at
start. Enums are stored by name (`Dorian`, `ChordRecognizer`); times as round-trip text in UTC.

- **Runs**: one verdict each. `Song` is the identity a later run supersedes (latest
  `RecordedAt` wins), compared without case. The verdict is `Confidence` (`High`, `Medium`,
  `Low`, `Withheld`) and a tonal center, `Tonic` (sharps only: `C#`, never `Db`) and `Mode`
  (one of the seven diatonic modes), both null only when withheld. `Mixed` marks two modes on
  one tonic, the center being the harmony's. `SourceUrl` is the upload the audio came from,
  null only with `LocalSource`. `WithheldReasoning` is the reasoning of the first pass when it asked for windows, kept once here. `Direction` is the user's note sent with windows. `Evidence`
  is the detectors' report as JSON. `OldBpm*` are the old estimates of imported history, never
  written again.
- **Sections**: a run's sections, each a `Label` with its own `Tonic` and `Mode`, and a `Start` in seconds into the track (null on runs recorded before ADR 0064).
- **Sources**: what the judge consulted. `Kind` is `Authoritative`, `Corroborating`,
  `Algorithmic`, `ChordRecognizer` or `Ear` (the user's own reading). `StatesKey` is what
  the source said as a 24-key name, `StatesMode` the mode it named, `Sections` the labels
  it covered as a JSON list (null when it did not say).
- **Windows**: stretches detected on their own, `Start` and `End` in seconds, with an
  optional `Label` and who asked (`User` or `Judge`; null on windows imported from history).
  `FirstBar` and `LastBar` are the bars the user gave, both included, where they gave bars,
  numbered as Live's ruler numbers them (ADR 0072; bars stored before 2026-10-31 were cleared);
  `Why` is the one sentence the judge gave for asking; on windows recorded before ADR 0064 it is a copy of the first pass's whole reasoning.
- **Candidates**: the keys the judge weighed besides the verdict's, each a `Key` as written and a `Why` of one sentence, in `Position` order.
- **Restatements**: "this upload is that song": a `SourceUrl` and the `RunId` whose verdict
  it carries over. It never changes an answer; the song's latest run is still its verdict.

The 24-key tag is never stored. It is the **collapse** of the center: the tonic kept, the
mode reduced to major or minor (A Dorian is tagged A minor).

## What is refused

A run is refused only where a wrong value would be
worse than no run:

- a confidence outside the four, or a center missing on a verdict that is not withheld;
- a tonic not in the sharp spelling, or a mode outside the seven;
- no source URL without `LocalSource`, or a source URL that is not http(s);
- a source kind outside the five;
- a song, artist or title still mis-encoded after repair. UTF-8 read as CP1252
  (`RÃ¶yksopp`) is repaired on the way in; what cannot be repaired is refused, because a
  damaged song identity is a second identity no later run can replace;
- a window that ends before it starts.

On the way in, a link is split from a trailing note (`… (via web search)`), a chord
recognizer's host (Chordify, ChordU) overrides whatever kind was written, and a source's
stated key is normalized to the 24-key spelling.

A restatement is refused for an upload the ledger has never seen. One for an upload the song
already has is a skip, not a failure.

## Where the rows come from

The judge writes here itself ([docs/judge.md](judge.md)). The rows before it were imported once from
a predecessor tool's JSONL, and the import is gone: `OldBpm*` and the windows with
no asker are what is left of it ([ADR 0055](adr/0055-crateside-owns-the-key-judge-and-its-ledger.md)).
