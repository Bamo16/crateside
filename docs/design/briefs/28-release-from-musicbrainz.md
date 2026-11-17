# crateside: fill release year, label and cover from MusicBrainz where a track's source has none

Issue: #185 (fill release year, label and cover from MusicBrainz)

For an implementing agent; Bamo reviews first. Terms (track key, source, stem) are in `CONTEXT.md`.
ADRs touched: 0077 (`Release`, what a source says), 0088 and 0089 (genre and familiarity already
call MusicBrainz), 0082/0083 (decisions in `ledger.db`). Feeds #159 (cover art, built from embedded
art in 5513813).

## Problem

A plain YouTube upload carries no album, label or release year; the song page shows the year as
"uploaded 2019" at best (`Release.Describe`, `Imports/Release.cs:39-58`). The issue measured
MusicBrainz on 2026-11-04 (library sample 17 of 20 matched, others 16 of 20, video game 1 of 5)
and decided that tags win field by field, the match needs a duration check, and existing tracks
are filled once in the background.

## Current behavior

- **`Release`** (`Imports/Release.cs:6-61`) holds Album, Year, `YearKind` (`Release` or `Upload`),
  `Organization` (the label tag) and `Cover` (a file name in the covers folder). It has no label
  from a database, release type or ISRC. `ReleaseYear` is null for an upload date.
- **It exists only for imports**: `TrackIntake.cs:199` writes it, and the intake row keeps it as JSON
  (`Ledger/DecisionRows.cs:275`). A playlist track has none.
- **`Release.Cover` is written but never read.** `TrackIntake.SaveCover` (`:225-244`) writes the
  covers folder; the only readers of `CoversFolder` are that writer. What the app shows comes from
  `CoverArt.FindAsync` (`Art/CoverArt.cs`), which extracts the picture embedded in a track's audio
  with ffmpeg, served by `Api/CoverArtEndpoint.cs` to `Components/Cover.razor`. For a YouTube
  track that picture is the thumbnail StemForge embedded (ADR 0077).
- **MusicBrainz is already called twice, with separate rate limits.** `GenreLookup.MusicBrainzAsync`
  (`Genres/GenreLookup.cs:58-105`: search `limit=1`, no duration check, 1.1 s between requests) and
  `FamiliarityLookup` (`Familiarity/FamiliarityLookup.cs:125`, 600 ms). Their workers run
  concurrently, so together they can exceed MusicBrainz's one request a second. The User-Agent is
  already `crateside/1.0 ( https://github.com/Bamo16/crateside )` (`CratesideHttp.cs:5`).
- `GenreStore` keeps the matched recording's id (`RecordingMbid`), taken from the top hit.
- A track's duration is readable from any of its audio files (`Samples/StemTagReader.cs:142`;
  `Beats/BeatReader.cs:445`); stems are full length, so a library track needs no source.

## What MusicBrainz returns (read-only, 2026-11-17, generic User-Agent, 1 request a second)

- A recording search for `Around the World` by Daft Punk returned **three hits all scoring 100**:
  a 2022 live album, a 287 s live mix and a 130 s track. The duration check is not optional.
- A hit's `length` is milliseconds and **can be null** (one of three here). A null length cannot be
  verified, so it is rejected.
- The search hit lists its `releases` with `date`; `recording/{id}?inc=releases+isrcs` also returns
  `first-release-date` (here `2011`). `isrcs` was an empty list for that recording.
- A Cover Art Archive request `release/{id}/front-250` answers **307** for a release that has art.
- For the one release checked, `release/{id}?inc=labels` gave an empty `label-info`: a label is not
  always there, as the issue found.

## Design

### 1. One matcher, one limiter

- A `MusicBrainz` domain folder in `Crateside.Core` with `MusicBrainzGate` (a singleton that spaces
  **every** MusicBrainz request, from all three lookups, 1.1 s apart) and `RecordingMatcher`
  (search `limit=10`, keep hits within **3 seconds** of the track's duration, null `length` rejected,
  then the highest score). `GenreLookup` and `FamiliarityLookup` swap their own delays for the
  gate; their matching is not changed.
- **Year** is the matched recording's `first-release-date` year. **Label, release type and cover** come
  from the **earliest official release** of that recording (status Official; an Album or Single
  before a Compilation), one `release/{id}?inc=labels+release-groups` request, `[no label]`
  treated as none.
- Requests per track: one search and one release lookup, about 2.5 seconds with the gate.
  ISRC is skipped: nothing reads it and `Release` has no field (see point 4).

### 2. Keeping it beside what the source said

`Release` keeps meaning "what the source said", as ADR 0077 has it. A new ledger table
`ReleaseFills` (track key, recording id, release id, year, label, release type, cover file name,
asked-at, outcome of `Filled`, `NoMatch` or `NoAnswer`) holds MusicBrainz's side, one EF migration
after `20261110212347_LiveAnswers`. A single reader merges field by field: the source's value if it
has one, else the fill's, and says which (`from the file's tags`, `from the upload`, `from
MusicBrainz`) so the song page's release line can mark a filled field. **A year of kind `Upload` counts as no year**, so
a YouTube track gains a release year, never replaces an upload date.

### 3. The cover

The cover of the chosen release is fetched from the Cover Art Archive (`front-500`) into the covers
folder as `{key}.jpg`, as `SaveCover` names files. `CoverArt.FindAsync` takes the fill's file
**before** a YouTube track's embedded thumbnail, and never before a picture in an import's own
tags. The picture is not written into the audio files here (the backing pass is separate).

### 4. When it runs

A `ReleaseFillWorker`, shaped like `GenreWorker` (`NewWork`, one track at a time, never paused),
takes every row with a non-null `ArtistAndTitle`, a duration and no recorded outcome, so a landed
change fills the existing library once, in the background, as the issue says. A `NoAnswer` is
retried up to three times; `NoMatch` is final until the track is swapped (`TrackSwap`).

## Cost

One agent, one session, no GPU, no quota. About 150 lines for the matcher and gate, 100 for the
fill store and migration, 120 for the worker and cover fetch, 40 in `CoverArt` and the song page's
release line, and tests; the two existing lookups change by a few lines each. The library
(roughly 600 tracks at 2.5 seconds each) fills in about 25 minutes of background time.

## Test plan

- `RecordingMatcherTests` on a captured search response in `Fixtures/`: the 287 s and 130 s hits
  against a 130 s file pick the 130 s hit; a null `length` is rejected; a 122 s file against a 245 s
  remaster matches nothing (the issue's video game case).
- `ReleaseFillTests`: tags win field by field; an upload-kind year is replaced by the fill's; a
  file with a label tag keeps it; `[no label]` is none.
- `MusicBrainzGateTests` with a `FakeTimeProvider`: two lookups racing are spaced 1.1 s apart.
- `CoverArtTests`: a YouTube track with a fill and an embedded thumbnail serves the fill; an import
  with its own picture serves its own.
- On the library: a YouTube track with no tags gains a year, a label and a cover on its song page;
  a video game track is left as its tags have it.

## Out of scope

AcoustID; instrumental signals (none worth using, per the issue); writing the cover or label into
the files; adding music MusicBrainz does not have; #159's other work.

## For Bamo to confirm

1. **A MusicBrainz cover replaces a YouTube track's embedded thumbnail on screen.** Recommended: the
   thumbnail is a video frame, and the issue's purpose is a real cover. Alternative: only fill
   where there is no embedded picture at all, which for YouTube is never.
2. **Fetch the cover from the Cover Art Archive with crateside's own HTTP.** The house rule says
   downloads go through `stemforge-cli`; this reads it as audio and video only, as the metadata
   lookups already use crateside's HTTP. Recommended: yes. Alternative: a StemForge change first.
3. **Label and cover come from the earliest official release of the matched recording.**
   Recommended: the original release, not a reissue or compilation. Alternative: the release with
   the best score.
4. **Skip ISRC for now.** Recommended: nothing reads it, and measuring it was 11 of 20. Alternative:
   add a field and a third request.
