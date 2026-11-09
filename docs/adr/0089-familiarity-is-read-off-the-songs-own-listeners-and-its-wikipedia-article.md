# 0089 - familiarity is read off the song's own listeners and its Wikipedia article

- Status: Accepted
- Date: 2026-11-09
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
  matches, so both share one search.
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
