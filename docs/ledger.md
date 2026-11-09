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
  (one of the seven diatonic modes), both null only when withheld. `DecidedBy` is who made the verdict:
  `Judge` (an agent session; every run recorded before the column, the imported history
  included), `Rules` (crateside's rules, which rule only on what the evidence and the pages agree on)
  or `Person` (the key chosen on the song page); ADR 0076. `Mixed` marks two modes on
  one tonic, the center being the harmony's. `SourceUrl` is the upload the audio came from,
  or `track:<key>` for a track that is not on YouTube (ADR 0077); null only with `LocalSource`. `WithheldReasoning` is the reasoning of the first pass when it asked for windows, kept once here. `Direction` is the user's note sent with windows. `Evidence`
  is the detectors' report as JSON. `Lookups` is what the lookup found on each site before the
  judge ran, as JSON: every page, cited or not, with the TheoryTab parsed from each Hooktheory
  page, and under `candidates` the pages found with the exact title under no artist the track credits,
  which no rule reads (ADR 0077); null on runs recorded before 2026-11-01 and where there was no
  artist and title to look up. `OldBpm*` are the old estimates of imported history, written
  again only by the person's run over an imported verdict, so its warp starting point stays.
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

## The person's decisions

Beside `Runs`, never inside it, the same database keeps what the person decided
([ADR 0082](adr/0082-the-persons-decisions-live-in-the-ledger-database.md)), so a ledger backup
restores both. They change; a run does not.

- **PageChoices**: a page rejected, counted or added for a track (ADR 0078), by `TrackKey`: `Url`,
  `Kind` (`Rejected`, `Promoted`, `Added`), `Page` (the page as the person saw it, JSON), `At` and
  `UndoneAt`. An undone choice stays.
- **SourceRemovals**: a recorded source taken off a track (ADR 0081), by `TrackKey`: `Source`, `At`,
  `UndoneAt`.
- **TrackMarks**: a mark on a song (`ChangesKey`, `OddBars`), one row each, by `Song`.
- **MeasuringExclusions**: a song, or stretches of it, not fit for measuring detectors (ADR 0085), by `Song`: one row with null `StartSeconds` for the whole track, else one row per stretch with `StartSeconds`, `EndSeconds` (seconds of the source), `Typed` and `Reason`.
- **Genres**: a song's genres ([ADR 0088](adr/0088-genre-is-abletons-own-genres-filled-from-the-sources.md)),
  by `Song`. `Heard` is every label the sources gave (a JSON list, in the lookup's order), kept as the
  song's styles; the genres crateside assigns are mapped from it when read, so the table can change
  without a new lookup. `RecordingMbid` is the MusicBrainz recording the lookup matched; `Learned` and
  `LearnedFrom` a genre taken from the person's answers for the artist; `Mine` the genres the person
  chose (a JSON list: among Ableton's 29, or their own); `Tagged` every genre crateside has asked to be
  written onto the song's files, so one it later drops can be taken off again.
- **Familiarities**: a song's familiarity ([ADR 0089](adr/0089-familiarity-is-read-off-the-songs-own-listeners-and-its-wikipedia-article.md)),
  by `Song`. `Listeners` is the most distinct ListenBrainz users any of its MusicBrainz recordings has (null
  when it has none); `Pageviews` the user views of its English Wikipedia article over the last twelve whole
  months (0 for no article); `WikidataItem` the item the article was found through. `LookedUpAt` and
  `LookupVersion` say when and by which matching it was asked; `Settled` is the person's answer. The
  value written is `Settled`, else what the two votes say of the numbers.
- **TrackInstructions**: what the person told crateside about a track
  ([ADR 0083](adr/0083-track-instructions-playlist-settings-and-intake-live-in-the-ledger-database.md)),
  by `TrackKey` (a video ID, case-sensitive): the stem plan (`Plan`, JSON), `Name`, `SourceUrl`,
  `DifferentFrom`, `AcceptedUpload`, `CutRequestedAt`, `AddedByUrl`, `Backfill`.
- **PlaylistSettings**: one row (`Id = 1`): `PlaylistId`, `RefreshMinutes`, `ConsumeOnArrival`.
  No row means configuration decides.
- **Intake**: one row per track crateside has taken on, in arrival order, `TrackKey` unique: what
  the playlist or the file said about it (`Title`, `ChannelTitle`, `AddedAt`, `PremiumOutlook`,
  `PageUrl`, `Artist`, `SongTitle`, `Release` as JSON) and whether its playlist entry was offered
  back (`RemovalAttemptedAt`, `RemovalFailureReason`, `RemovalFailureCanSucceedLater`,
  `RemovedEntryId`).

A `Song` is the song identity, or the upload's video ID for a song never judged; it moves to the song
when the verdict arrives (ADR 0071). At start, a pending `page-choices.json`, `track-marks.json`,
`genres.json`, `queue.json` or `intake.json` is imported once into an empty table and renamed
`<name>.imported-<yyyyMMdd>`.

The 24-key tag is never stored. It is the **collapse** of the center: the tonic kept, the
mode reduced to major or minor (A Dorian is tagged A minor).

## What is refused

A run is refused only where a wrong value would be
worse than no run:

- a confidence outside the four, or a center missing on a verdict that is not withheld;
- a tonic not in the sharp spelling, or a mode outside the seven;
- no source URL without `LocalSource`, or a source URL that is neither http(s) nor a `track:<key>`;
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

## The work key is not always a video ID

A track's **work key** is the one string the queue, the working set, intake and a stem's provenance
join on. A YouTube track's is its video ID; any other track's is `<source>~<id>` (`soundcloud~242136202`,
`file~3fa9c0d12be84471`), and its `SourceUrl` and every stem's `source:` read `track:<key>`
([ADR 0077](adr/0077-a-track-that-is-not-on-youtube-is-keyed-by-its-source-and-named-from-its-tags.md)).
`TrackKeys` in `Crateside.Core` is the one place that knows these shapes. **Nothing stored changed
name**: the JSON files and API keep `videoId` and `sourceVideoId`, and the ledger's columns are the
ones above, so a stored key is read as before whatever it holds. The intake (once `intake.json`) gained optional
`pageUrl`, `artist`, `songTitle` and `release` fields on a record, which older readers ignore, and
`recipe.json` is new.
