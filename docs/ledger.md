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
| the export, for reading by eye | `ledger.jsonl` beside it, rewritten after every change |

None of it is in git. A backup is a whole database (SQLite's backup API), so restoring one is
copying it over `ledger.db` while crateside is stopped.

## The tables

The schema is EF Core's, changed only by a migration in `Ledger/Migrations/` and applied at
start. Enums are stored by name (`Dorian`, `ChordRecogniser`); times as round-trip text in UTC.

- **Runs**: one verdict each. `Song` is the identity a later run supersedes (latest
  `RecordedAt` wins), compared without case. The verdict is `Confidence` (`High`, `Medium`,
  `Low`, `Withheld`) and a tonal centre, `Tonic` (sharps only: `C#`, never `Db`) and `Mode`
  (one of the seven diatonic modes), both null only when withheld. `Mixed` marks two modes on
  one tonic, the centre being the harmony's. `SourceUrl` is the upload the audio came from,
  null only with `LocalSource`. `Direction` is the user's note sent with windows. `Evidence`
  is the detectors' report as JSON. `OldBpm*` are keytag's old estimates, imported and never
  written again. `KeytagLine` is the timestamp of the keytag line a run was imported from.
- **Sections**: a run's sections, each a `Label` with its own `Tonic` and `Mode`.
- **Sources**: what the judge consulted. `Kind` is `Authoritative`, `Corroborating`,
  `Algorithmic`, `ChordRecogniser` or `Ear` (the user's own reading). `StatesKey` is what
  the source said as a 24-key name, `StatesMode` the mode it named, `Sections` the labels
  it covered as a JSON list (null when it did not say).
- **Windows**: stretches detected on their own, `Start` and `End` in seconds, with an
  optional `Label` and who asked (`User` or `Judge`; null on windows imported from keytag).
- **Restatements**: "this upload is that song": a `SourceUrl` and the `RunId` whose verdict
  it carries over. It never changes an answer; the song's latest run is still its verdict.

The 24-key tag is never stored. It is the **collapse** of the centre: the tonic kept, the
mode reduced to major or minor (A Dorian is tagged A minor).

## What is refused

Carried over from keytag's `ledger.py`. A run is refused only where a wrong value would be
worse than no run:

- a confidence outside the four, or a centre missing on a verdict that is not withheld;
- a tonic not in the sharp spelling, or a mode outside the seven;
- no source URL without `LocalSource`, or a source URL that is not http(s);
- a source kind outside the five;
- a song, artist or title still mis-encoded after repair. UTF-8 read as CP1252
  (`RÃ¶yksopp`) is repaired on the way in; what cannot be repaired is refused, because a
  damaged song identity is a second identity no later run can replace;
- a window that ends before it starts.

On the way in, a link is split from a trailing note (`… (via web search)`), a chord
recogniser's host (Chordify, ChordU) overrides whatever kind was written, and a source's
stated key is normalized to the 24-key spelling.

A restatement is refused for an upload the ledger has never seen. One for an upload the song
already has is a skip, not a failure.

## The bridge from keytag

Until the judge writes here itself (#111, the judge rewrite), verdicts still arrive through keytag's
recorder in its `results/runs.jsonl` (`Paths:KeytagLedger`). crateside imports every line it
does not yet hold whenever that file changes, so a verdict a session just recorded is visible
at once. It never writes the file. A line it cannot take is listed in
`LedgerStore.ImportRefusals`. Imported, a `key` becomes its tonic on Ionian or Aeolian, or on
the mode the line stated.

One line of the history is left out: the 2026-08-01 "segment reanalysis" of *A Horny Money
World*, which was re-recorded unchanged under the song's own identity on 2026-08-12.
