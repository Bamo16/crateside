# 0077 - a track that is not on YouTube is keyed by its source and named from its tags

- Status: Accepted
- Date: 2026-11-04
- For [#182](https://github.com/Bamo16/crateside/issues/182). Extends
  [ADR 0014](0014-the-song-is-the-identity-and-the-name-is-not.md) (the work key is no longer
  always a video ID), [ADR 0021](0021-the-playlist-is-a-message-queue-consumed-on-arrival.md) (a
  second inbox, a folder) and [ADR 0061](0061-a-source-download-is-kept-for-a-set-time-after-its-stems-reach-the-library.md)
  (an imported file is never pruned).
- Decided with the maintainer: no batches, a recipe per inbox folder, and a vocal flag that only
  offers.

## Context

Every track crateside knows is a YouTube upload, and its video ID is the key for work: the queue,
the working set, intake, the ledger's join onto a stem, and the stem's own tags. The first wanted
non-YouTube source is video game music, as tagged FLAC rips downloaded from soundtrack sites, but
the design should take any file or any site yt-dlp reads.

ADR 0014 already separates three things: the **song** (the ledger's identity, fixed at the first
run), the **name** (what is shown) and the **work key** (which upload the work is for). Only the
work key is YouTube-shaped, so only it changes here.

## What was measured, 2026-11-04

**What yt-dlp gives** (`--dump-json --skip-download`, yt-dlp 2026.09.27):

| | SoundCloud | Bandcamp | plain YouTube upload |
|---|---|---|---|
| `(extractor_key, id)` | `Soundcloud`, `242136202` | `Bandcamp`, `118527279` | `Youtube`, `NTa6Xbzfq1U` |
| artist | none (`uploader` only) | yes | none (`uploader` only) |
| track title | `track` | `track` (`title` is "Artist - Title") | `title` only |
| album, album artist, track number | none | yes | none |
| `release_year` | 2016 | 1998 | none |
| `upload_date` | yes | yes | yes |
| genre | wrong (the album's name) | none | none |
| square cover | yes | yes | no (a video frame) |

Only the key, the page URL, a title, the uploader, the duration and an upload date come from every
source. A soundtrack site's direct link gives the generic extractor and the filename alone, but the
file's own tags are good, and a ranged read of the first ~1 KB gives them.

**The Hooktheory lookup on five tagged VGM files** (DDR EXTREME, Super Mario RPG ×2, a Sonic 2
fan remake, Donkey Kong Country 2):

- today's lookup (direct slug, then a search on "artist title" with the slug and `SameArtist`
  checks) finds **0 of 5**;
- a title-only search, accepting a hit whose artist loosely matches any of the artist, album
  artist, composer or organization tags, finds **1 of 5** (Dave Wise as `david-wise`), with no
  wrong hit;
- of the other four, two have no page found at all, one is filed under another title for the song
  (`yoko-shimomura/forest-maze` for "Beware the Forest's Mushrooms"), and one under the publisher
  (`sega/chemical-plant-zone`), which a search for "Chemical Plant Zone" without " - Act 1"
  returns first.

**Near-silent vocal stems.** The loudness stage keeps one integrated loudness per stem
(`loudness.json`). Of the 46 tracks the maintainer cut down to the source, 13 ever had a vocal stem
and 4 of those still have its measurement. Against the 572 ordinary tracks' vocal stems:

| | n | vocal stem, LUFS | vocal − instrumental, dB |
|---|---|---|---|
| cut to the source | 4 | −70.0, −36.7, −28.6, −27.4 | −55.0, −24.0, −17.1, −17.2 |
| ordinary | 572 | p1 −24.6, median −14.8 | p1 −12.7, median −2.0 |

Below −16 dB relative, all four are caught and two ordinary tracks are flagged (PIKASONIC - Inside,
Nine Inch Nails - La Mer, both largely instrumental); the next ordinary track sits at −14.0. An
absolute cut-off does worse: below −27 LUFS flags three, and the nearest ordinary track (−27.2) is
0.2 dB from the closest labelled one. Four labelled examples are few, so the threshold is a
constant to revisit, not a finding.

## Decision

### The work key

**A track's work key stays one string.** A YouTube track keeps its bare video ID, so nothing
stored today changes and no user file is migrated. Any other track's key is
`<source>~<id>`:

| source | key | example |
|---|---|---|
| YouTube, any upload | the video ID, as today | `NTa6Xbzfq1U` |
| any other site yt-dlp reads | yt-dlp's `extractor_key`, lower case, `~`, its `id` | `soundcloud~242136202` |
| a local file, or a direct link to one | `file~` and the first 16 hex digits of the SHA-256 of the file as received | `file~3fa9c0d12be84471` |

`~` is outside YouTube's ID alphabet, so a key never reads as a video ID, and it is safe in a file
name, a URL path (`/song/soundcloud~242136202`) and JSON. A direct link is keyed by its content,
not its URL, because yt-dlp's generic ID is only the filename. The same file dropped twice is the
same track and is not taken in again.

`TrackKey` (in `Crateside.Core`) is the one place that knows these shapes, as `YouTubeUrl` is for
video IDs. `TrackKey.TryParse` accepts everything `YouTubeUrl.TryGetVideoId` accepts and also the
`track:<key>` form below; it replaces `TryGetVideoId` at every call site that wants a work key.
`YouTubeUrl` stays for what only YouTube has (the canonical watch URL, the premium check).

**In code the work key is renamed `TrackKey`** wherever it is held (`WorkOrder.VideoId`,
`IntakeRecord.VideoId`, `StemProvenance.SourceVideoId` and their kin), because a field called
`VideoId` holding `file~…` would mislead. **Stored names stay**: JSON properties keep `videoId`
through `[JsonPropertyName]` and the ledger keeps its columns, so the files and the database are
unchanged. `docs/ledger.md` says so.

### The key travels in the provenance

A stem joins its track through the `source:` part of its provenance comment, and StemForge copies
that part from its input file onto every stem it cuts (which is how a kept source is cut today).
So for a non-YouTube track **crateside stamps its own copy of the source with
`source: track:<key>`**, in a `crateside/<version>` comment as for an adopted file (ADR 0022), and
everything downstream (handoff, tagging, the ledger's join) reads the key through
`TrackKey.TryParse` with no new machinery. The file's own comment, if it had one, is kept in the
release metadata below. The ledger's `Runs.SourceUrl` for such a track is the same `track:<key>`,
so the one parse serves both. The page the audio came from is kept in the intake record, not in the
provenance.

### Where the audio is kept

- **A site track** (`soundcloud~…`) is downloaded by `stemforge-cli download` into the working set
  as today, then re-stamped. It can be fetched again, so ADR 0061's retention applies unchanged.
- **A local file or a direct link** is kept in `imports\<key>\<original file name>` beside the
  working set. crateside then holds the only copy it can rely on, so **an import is never pruned**
  by retention or the janitor. An album of FLAC costs 0.5 to 1 GB. A direct link is fetched by
  crateside itself (plain HTTP, after reading the head with a range request to name the track at
  once), since there is nothing for yt-dlp to add.

### Intake, one track at a time

Three ways in:

1. **The inbox folder**, `Paths:Inbox`, default `%USERPROFILE%\Music\crateside inbox`. crateside
   consumes it: each audio file (`.flac .mp3 .m4a .ogg .opus .wav .aif .aiff`) is moved into
   `imports`, once its size has held still for five seconds and it opens for exclusive reading, so a
   browser's half-written download is left alone (and `.part`, `.crdownload`, `.tmp` are ignored).
   A `.zip` is extracted into `imports` and then deleted from the inbox. Anything else is left where
   it is and named in the log.
2. **Dropping files onto Tracks.** The files are copied; the originals are left alone.
3. **Pasting links** into "Add a track". A YouTube link behaves exactly as today. Any other link is
   dumped with yt-dlp first: the generic extractor on a media file makes it a direct link, anything
   else a site track.

**crateside never deletes a file outside its inbox and its own folders.**

**There are no batches.** Each file is its own track and its own decision. Grouping by album was
weighed and dropped: the only thing a batch would do is apply one choice to its members, and an
ordinary album mixes instrumentals and songs, so a shared choice is wrong for some of them. The
album is still kept per track (below). A per-track signal before separation (an instrumental mark
from a lyrics or release database) is the better answer and is measured separately.

### Recipes, and folders that name one

A **recipe** is a named plan. Two ship: **Standard** (`StemPlan.Default`) and **Instrumental**
(`StemPlan.InstrumentalRelease`: the source and the drum guide). Settings chooses the **default
recipe**, which a YouTube track and a file saved straight into the inbox, dropped or pasted take.
**An inbox subfolder whose name is a recipe's** (case-insensitive: `crateside inbox\Instrumental\`)
gives its files that recipe; a subfolder with any other name is read as the inbox itself. The
recipe sets the track's plan at intake, and the person's later edits to the plan win, as today.
Defining recipes of one's own is left for a later change; the folder rule is already shaped for it.

### Names from tags

The name is `ARTIST - TITLE` from the file's tags, several `ARTIST` values joined with `, `
(ADR 0014's name, so it can be changed later like any other). A site track uses yt-dlp's `artist`
and `track` where present, then `uploader` and `title`. With neither, the filename with its
extension and a leading track number (`1-01. `, `19. `, `1-28 - `) removed. The ledger's song slug
is made from that name at the first run, as today.

### Release metadata

Kept on the intake record as `Release`, whatever the source, and shown only where it helps (the
song page's header line, and the API):

- album, album artist, track and disc numbers and totals, genre, composer, organization, and the
  catalogue number a gamerip carries (`ost`);
- **a year that says what it is**: `Year` with `Kind` `Release` (from a tag's `DATE`, the first
  four-digit year in it, the text kept as written; or yt-dlp's `release_year`) or `Upload` (from
  `upload_date`, used only when there is no release year, and never shown as a release year);
- **the cover**, extracted once to `covers\<key>.<jpg|png>` (beside the working set), from the
  file's embedded picture or the site's square thumbnail; a plain YouTube frame is not a cover.
  #159 uses it.

A remake's year is the remake's (the Sonic 2 file's 2020), because it is another recording.
Existing YouTube tracks gain `Release` only if a later change fills it; this ADR does not backfill.

### The page lookup

For every track, using whichever names it has:

- **Search by title alone** as well as by artist and title, with the title's last " - Act N",
  " - Part N" or bracketed tail removed as the bracket is today.
- **Accept a hit whose artist matches any of the artist, album artist, composer or organization**,
  loosely: the comparisons `SameArtist` makes today, plus the same surname with the same first
  initial (Dave Wise, David Wise).
- **A hit with the exact title and no artist match is kept as an unconfirmed candidate** in
  `Runs.Lookups`, shown to the judge and on the song page, and **never used by the rules**
  (ADR 0076). That surfaces the Sega page above without letting a stranger's page rule a key.

A page under another title for the song is left to the judge or the person.

### What a non-YouTube track skips

The premium check, the oEmbed availability check and playlist removal are YouTube's; they are
skipped for a key that is not a video ID. The Deezer genre and familiarity lookups still run; where
they find nothing, the tag's genre stands in for genre. *Swap source* works for any key (a VGM track
can later be swapped to a YouTube upload, keeping its song).

### A near-silent vocal stem says so

After the loudness stage has measured a track's stems, **a vocal stem more than 16 dB quieter than
its instrumental is flagged** on the track's row, collapsed or not, for every track whatever its
source: "vocals near-silent", with one click to switch the track to the Instrumental recipe (the
re-cut keeps the source and the drum guide and drops the vocal and instrumental). Nothing switches
by itself. The threshold is a named constant, to revisit as more tracks are cut down to the
source.

## Consequences

- **Every existing track is untouched**: its key, its files, the ledger and the queue read as
  before. The work is a rename in code, one parser, and new paths for things that did not exist.
- **The stems of a non-YouTube track carry `source: track:<key>`**, not a web address. The page it
  came from is on the intake record and the song page.
- **Imports grow without bound** until the person removes a track; Settings shows their size.
- **The lookup change reaches YouTube tracks too.** Only through the title-only search and the
  initial-and-surname match, and a looser hit can only send a track to the person (ADR 0076).
- `docs/ledger.md`, `docs/api.md` and `docs/openapi.json` follow the rename of what an ID can be.
