# 0088 - genre is Ableton's own Genres, filled in from the sources

- Status: Accepted
- Date: 2026-11-09
- Built by [#190](https://github.com/Bamo16/crateside/issues/190).
- Supersedes [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md). Amends
  [ADR 0033](0033-a-genre-can-be-learned-from-the-users-answers-for-an-artist.md). Follows
  [ADR 0087](0087-crateside-tags-every-song-and-the-person-corrects-in-crateside-or-in-live.md).

## Context

ADR 0030 chose a short closed list of seven under a group of crateside's own and wrote only what
the user settled. Measured 2026-11-09:

- **The sources name far more than seven.** The lookup's kept labels for 687 songs hold 274
  distinct labels; 137 appear on three or more songs, 49 on ten or more.
- **Live keeps a tag defined after nothing uses it** (a test group with a tag and a subtag, no file
  tagged, 2026-11-09). Writing styles as tags would grow the browser's list with every style ever
  written.
- **Live nests one level**: a tag can take subtags, and a subtag cannot (`Clips|Drum Clip|Full Drum
  Clip`, and the 2026-11-09 test).
- **Live's own content already carries Ableton's built-in `Genres` tags** in this library: 54 files
  `Genres|Hip Hop`, 38 Funk, 28 House, and every one of its 29 tags is in use somewhere (ADR 0030
  counted 31).

## Decision

**crateside writes Ableton's built-in `Genres` group, its 29 tags as Ableton ships them, filled in
from the sources. Styles are kept in crateside and never written as tags.**

- **The list is Ableton's**, unextended by crateside: Ambient, Blues, Breakbeat, Dancehall, Disco,
  Drum & Bass, Dub, Dubstep, EDM, Electro, Electronica, Experimental, Folk, Funk, Garage, Hip Hop,
  House, Indie, Jazz, Latin, Pop, R&B, Reggae, Reggaeton, Rock, Soul, Synthpop, Techno, Trap. One
  filter then finds the person's stems and Live's own content together.
- **Up to two per song** from crateside (pop rap is Hip Hop and Pop); a song that is clearly one
  thing gets one.
- **Sources are keyless**: MusicBrainz tags (recording, then artist), then Deezer's and iTunes'
  single genre. A table shipped with crateside maps each label onto the 29; a label it does not map
  is kept, and the Library page lists unmapped labels most common first, so the table grows where
  libraries need it.
- **Styles** (the sources' finer labels, such as `boom bap` or `kawaii future bass`) are kept and
  shown on the song page. They are not tags.
- **The person's own genres** go in the same group (`Genres|Meme`). crateside never assigns one,
  never learns one from an artist, and does not count it toward the two. Adding one leaves
  crateside's in place; a correction among the 29 replaces crateside's. The song page offers the 29,
  every genre of the person's own already in use, and a new one.
- **Learning from an artist (ADR 0033) is kept unchanged**, and counts only the person's answers
  among the 29. It now fills only songs whose own sources map onto nothing.
- **Start over.** The `Genre|` group retires: its keywords are taken off, and every answer and
  learned genre in the ledger's `Genres` table is dropped, the 282 answers included. The migration's
  `before-<migration>.db` copy keeps them.

## Considered options

- **Discogs' 15 genres with their styles as subtags.** Established and two levels, but styles as
  tags grow without bound in the browser (above), and Discogs' own data needs a personal token.
- **A list of crateside's own, extended** (ADR 0030, widened). It fits the maintainer's taste, but
  someone has to own and defend it, and other users may not share it.
- **Ableton's list plus crateside additions** (Future Bass, K-Pop). Additions bring back a list to
  maintain; the person can add their own instead.

## Consequences

- K-pop, J-pop and future bass have no tag of their own: they get what they sound like (Pop, Hip
  Hop, EDM, Electronica). A person who wants one adds it as their own.
- The expanded `Genres` group shows all 29 whether used or not, as Ableton's own group always does.
- A wrong mapping tags many songs at once; it is fixed once in the table.

## Built

Built by [#190](https://github.com/Bamo16/crateside/issues/190) on 2026-11-09. Where the build had to
choose:

- **Two per song** are the two genres the most distinct labels map onto, the second only when at
  least half as many labels back it as the first; a tie goes to the label heard first. The lookup's
  order is MusicBrainz recording, MusicBrainz artist, Deezer (the album's genres, on the same track
  match familiarity uses), iTunes.
- **The table** (`genre-labels.json`) covers every label that was on three or more songs on
  2026-11-09, except four that name no genre (`soundtrack`, `comedy`, `instrumental`, `ballad`);
  those stay on the Library page's list. Deezer's own genre names, which no lookup had kept yet,
  were entered from what Deezer is known to call them and are **unmeasured** until the lookup has
  run.
- **A genre dropped later is taken off the files** only if crateside asked for it before: the song's
  record keeps every genre it has asked to be written (`Tagged`). That is how a `Genres|` tag of
  Live's own stays, and how a changed mapping or the person's replacement takes crateside's off.
- **The migration** (`GenresFromAbleton`) drops the answer, learned and suggested columns, clears
  `LookedUpAt` so every song is asked again, and keeps `Heard`, so songs show their genres from the
  labels already kept until the new lookup runs. The ledger's `before-<migration>.db` copy is made by
  the ledger before any migration applies.
