# 0071 — A record kept under an upload follows it to its song

- Status: Accepted
- Date: 2026-10-30
- Amends [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md) ("a song the
  ledger never judged is keyed by its upload") and applies
  [ADR 0014](0014-the-song-is-the-identity-and-the-name-is-not.md) to the per-song stores.
- Amended 2026-11-08 by [ADR 0082](0082-the-persons-decisions-live-in-the-ledger-database.md): the
  records are rows in `ledger.db`, so the `.bak-` copy of a file before a move is gone; the
  start-up ledger backup covers it.

## Context

Genre (`genres.json`), familiarity (`familiarity.json`) and marks (`track-marks.json`) are keyed
by song identity, or by the video ID while the ledger has no verdict for the upload. Nothing moved
a record when the verdict arrived. The row then read under the song, found nothing, and the record
under the video ID was stranded.

Taylor Swift – *Style* (`hYt5NSjWKds`) showed it on 2026-10-30. **Measured** in that day's log
and stores: its genre was looked up at 21:38 UTC under the video ID, it was judged at 22:07 as
`taylor swift - style`, and its song page then read Genre "not looked up yet", with no choices to
click, because the strip offers none for a song with no lookup. Familiarity showed only because
its worker had looked the song up a second time under the new key; the genre worker had not yet.

**Measured** the same evening, against a copy of the ledger: 171 of 771 genre records and 191 of
794 familiarity records were keyed by a video ID the ledger had since judged. 20 of the genre
records were the user's own answer under a song whose record held none, so the row showed the
song unanswered and the backing pass wrote no genre for it. No marks were affected.

## Decision

- **A record follows its upload to its song.** Whenever the rows are read, each genre,
  familiarity and mark record whose key the ledger resolves to a song moves under that song.
  Keying by video ID throughout was not chosen: ADR 0014 makes the song the identity, and every
  reader (rows, backing, the artist learning of ADR 0033) already reads by song.
- **Merging, when the song already has a record:** the user's own answer wins wherever it was
  given, the song's before the upload's; otherwise the song's record, the later one, is kept, with
  the upload's lookup filling in only where the song has none. Marks are the union.
- **At startup the same move runs once as a migration**, copying each file it changes to
  `<file>.bak-<yyyyMMddHHmmss>` beside it first. A second run moves nothing and copies nothing.

## Consequences

- A genre or familiarity answered before judging, or a suggestion looked up before it, shows on the
  judged row at once, and nothing is looked up twice.
- The 20 recovered genre answers reach the library at the next backing pass.
