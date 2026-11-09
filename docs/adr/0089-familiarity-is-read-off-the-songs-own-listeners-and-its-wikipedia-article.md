# 0089 - familiarity is read off the song's own listeners and its Wikipedia article

- Status: Accepted
- Date: 2026-11-09
- Built by [#191](https://github.com/Bamo16/crateside/issues/191); re-measured by [#196](https://github.com/Bamo16/crateside/issues/196).
- Supersedes [ADR 0032](0032-familiarity-is-read-off-reach-artist-first-then-song.md). Follows
  [ADR 0087](0087-crateside-tags-every-song-and-the-person-corrects-in-crateside-or-in-live.md).

## Context

Familiarity is used to pair a mashup's halves: a vocal the listener knows over an instrumental they
probably do not, or the reverse. ADR 0032 read it off Deezer, the most-followed credited artist
first and then the song's rank, with two values and the person answering the rest.

In use it put known artists' lesser songs on the known side (Disclosure – *Waterfall*, `Mainstream`
through the artist where Disclosure – *Latch* rightly is), and scene favourites there too (aespa –
*Spicy*, Busy P – *Genie*, which the listener is unlikely to know).

[The note of 2026-11-09](../notes/2026-11-09-what-tells-a-known-song-from-an-unknown-one.md)
measured keyless signals against 52 songs the maintainer had labelled known or not. Leave-one-out:

- ListenBrainz distinct listeners of the recording, 85%; the song's own English Wikipedia article,
  81% as yes or no and 77% by its pageviews.
- Deezer track rank and artist fans, 68% each.
- Wikidata's *charted in* is empty for songs, even *Rolling in the Deep*, so chart history is not
  available as data.
- Artist-level signals cannot tell a known artist's hit from their deep cut.

## Decision

**Familiarity is how likely the audience is to recognise the song, in three values, `Mainstream`,
`Niche` and `Obscure`, read off the song's own signals. Deezer is not used.**

- **One axis.** The vocal side filters `Mainstream`, the instrumental side `Obscure`. `Niche` is
  known within a scene but not generally, and is in neither filter.
- **Two votes.** ListenBrainz listeners of the song's recording ≥ 3,500 is one; the song's own
  English Wikipedia article ≥ 20,000 pageviews over the last year is the other. Both →
  `Mainstream`, neither → `Obscure`, one → `Niche`. No ListenBrainz match or no article is a "no"
  vote, so every song with an artist and a title gets a value. On the 52, this put 42 on the right
  extreme, 6 in `Niche` and 4 on the wrong one.
- **The cuts are fitted on those 52** and live in one method.
- **The recording** is the MusicBrainz recording the genre lookup ([ADR 0088](0088-genre-is-abletons-own-genres-filled-from-the-sources.md))
  matches, so both share one search. (Built differently: see Built.)
- **The article** is found through Wikidata: an item for a song whose performer is the song's
  artist, and its English Wikipedia sitelink. Searching Wikipedia by title is not used: it matched
  *Waterfall* to another band's song.
- **The artist plays no part.** A deep cut by a big artist reads by its own numbers. A correction
  stays on its song and teaches nothing about the artist's other songs (*Latch* known, *Waterfall*
  not). There is no audience profile; the person's corrections are the audience.
- **Start over.** Every row of the ledger's `Familiarities` table is recomputed, and the person's 45
  answers are dropped: they were given against two values. The migration's `before-<migration>.db`
  copy keeps them, and the note lists them as its ground truth.
- **Measured again** once the person's corrections number about a hundred, to settle whether
  ListenBrainz alone would do better than two votes.

## Considered options

- **Two concepts**, recognition for the vocal and *not overplayed* for the instrumental. The
  instrumental side's only counterexamples were a matching bug, fixed separately (lookup version 3),
  and *worth sharing* is the person's taste, which no lookup supplies.
- **ListenBrainz alone**, at one cut for known and one for not. Slightly better on the 52, but each
  middle cut would be a guess; two independent sources disagreeing is a reason for `Niche` that
  can be shown.
- **Keep Deezer as a third vote.** It was the weakest signal measured.

## Consequences

- Songs with a small online footprint that a scene knows well read `Obscure` (*NO CAP*, *Bumpin
  Bumpin*), and an old hit that is not known to this audience can read `Mainstream` (*Freestyler*).
  Those are the person's to correct.
- ListenBrainz's listeners skew to its own users, who are not a general audience. Unmeasured beyond
  the 52.
- A song with no Wikipedia article can never read `Mainstream`.

## Built

Built by [#191](https://github.com/Bamo16/crateside/issues/191) on 2026-11-09. Where the build differs
from the decision, and where it had to choose:

- **The listeners are the most any recording of the song has, not the genre lookup's recording.** The
  decision shares the genre lookup's one MusicBrainz match. Measured on the 52 with that first hit,
  one of the 22 known songs read `Mainstream` (the note's table has 15): a song's listens are spread
  over its releases and the first hit is as often an unplayed one (*Rolling in the Deep*,
  *Hello*, *Bye Bye Bye* all read no listeners). The note took the best-listened of up to 100 hits, so
  the lookup does: it asks MusicBrainz for up to 100 recordings of the title by the first credited
  artist, keeps those whose title is the song's and whose credit names one of its artists, asks
  ListenBrainz for all of them in one request and takes the largest user count. The familiarity
  lookup therefore does not wait for the genre lookup, and the recording the genre lookup stores is
  not used by it. With this, the 52 read 14 / 4 / 4 known and 1 / 2 / 27 unknown against the note's
  15 / 4 / 3 and 1 / 2 / 27.
- **The cuts are 3,500 and 20,000**, the round figures the issue gave, where the note fitted 3,481
  and 20,461. On the note's numbers this moves one song, *Ooh La La* (3,481 listeners), from `Niche`
  to `Obscure`.
- **The article** is found by Wikidata's own search, restricted to items that have a performer
  (`haswbstatement:P175`), for the exact title with its brackets taken off. A candidate counts when
  its English label or alias is that title once squashed to letters and digits, it is a song,
  single, musical work or musical composition (an album of the same name is passed over), and one of
  its performers' labels or aliases is one of the song's credited artists. Of several, the first with
  an English Wikipedia link wins. English Wikipedia holds most songs' articles on the musical-work
  item, not the single's, which is why those types are all accepted.
- **Pageviews** are the user agent's all-access monthly views over the twelve whole months before the
  current one; an article the API has no record of reads 0.
- **Pacing** is 0.6 s between requests to ListenBrainz, Wikidata and Wikimedia and 1.2 s before
  MusicBrainz, with crateside's User-Agent. A song takes six requests, about ten seconds with the
  servers' own time. ListenBrainz refuses a request body sent chunked, and answers `null` counts for
  a recording nobody played; both are handled.
- **The migration** (`FamiliarityFromListeners`) deletes every row and swaps the Deezer columns for
  `Listeners`, `Pageviews` and `WikidataItem`. The person's 45 answers are in the ledger's
  `before-<migration>.db` copy. `familiarity.json` is no longer imported.
- **Deezer** stays in the genre lookup only, which now owns its matching (`DeezerMatch`); the name
  matching the two lookups share is `NameMatch`.
- **Until a song is asked again**, the `Familiarity|` keyword already on its files stays; the backing
  pass replaces it when the song has a new value.
