# 0087 - crateside tags every song, and the person corrects it in crateside or in Live

- Status: Accepted
- Date: 2026-11-09
- Built by [#192](https://github.com/Bamo16/crateside/issues/192) (answers from Live) and [#193](https://github.com/Bamo16/crateside/issues/193) (the song page).
- Amends [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md) (only a settled
  genre is written) and [ADR 0032](0032-familiarity-is-read-off-reach-artist-first-then-song.md)
  (the user answers the rest). Applies [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md)'s
  stance to the descriptive tags. Used by ADRs 0088 (genre), 0089 (familiarity) and 0090 (lead or
  sparse vocal).

## Context

Genre and familiarity were built to settle the obvious cases and ask the person about the rest. On
2026-11-09 the ledger held 399 songs with no genre answer, and the familiarity rule asked about 113
songs and found no Deezer match for 97 more. A user arriving with a library expects it tagged, and
steps in only to correct. The maintainer also notices a wrong tag while working in Live, not in
crateside, and tags there.

## Decision

**Every descriptive tag (genre, familiarity, lead or sparse vocal) is written by crateside wherever
it has a signal, and the person corrects it in either place.**

- **No queue for the person.** Nothing waits for an answer. A song with no signal at all gets no
  tag, never a guess.
- **Where a value came from is shown** on the song page: looked up, measured, learned, or *your
  call*.
- **The person's answer always wins** and survives every later lookup or measurement.
- **A tag the person adds in Live is their answer.** The backing pass reads the index; a keyword of
  one of these groups that crateside did not write is recorded as the person's answer. Genre and
  familiarity belong to the song, so the answer is copied to the song's other samples. Lead or
  sparse belongs to the stem file and stays on it.
- **A tag the person removes in Live is a rejection.** crateside sees it as a hidden keyword (as
  today), never writes it back, and the song page shows *you removed it*.
- **The newer answer wins** when crateside and Live disagree. Live's answer arrives late: the index
  follows a change by minutes to hours ([ADR 0013](0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)).

## Considered options

- **Correct in crateside only**, leaving Live edits alone. Simpler, but a tag set in Live would
  never reach the song's other samples or teach anything, and Live is where a wrong tag is seen.
- **Keep asking about unclear songs.** It keeps wrong tags out, but leaves a third of a library
  untagged until the person works through a list, which a public user will not do.

## Consequences

- Some written tags are wrong until corrected. The source line on the song page is what makes them
  findable.
- A person's answer recorded from Live is only as good as the backing pass's read of the index, so
  a sample that is not keyed to a song (no provenance) cannot spread its answer.

## Built

[#192](https://github.com/Bamo16/crateside/issues/192) built the Live half on 2026-11-10; the song
page is [#193](https://github.com/Bamo16/crateside/issues/193).

- **Added**: a `Genres|` or `Familiarity|` keyword on a keyed file, in the tag store or Live's index,
  that crateside did not ask to be written (the song's `Tagged`, less what it was told to drop). A
  genre goes through the same choice the song page makes, so a pick among the 29 replaces
  crateside's and the person's earlier pick, and a genre of their own is added beside them. A
  familiarity is the song's `Settled`. Both are then written onto the song's other samples.
- **Removed**: a hidden keyword that crateside asked for. The song keeps a `Rejected` list; a
  rejected genre or familiarity is not assigned or written again, and the copies on the song's other
  samples are taken off. A later choice in crateside clears it. `RemovalsSeen` keeps each removal
  already acted on (stem file and keyword), so a hidden keyword the store still holds does not undo
  that choice, and a removal on another sample still counts.
- **Newer wins** comes out of the order the pass sees things in: an answer is recorded when the pass
  first sees it, and each recorded answer replaces the one before. No time is stored, so an answer
  given in Live and read after one made in crateside wins, even if Live's was older.
- **Familiarity** gained a `Tagged` list for this. The migration seeds it with the value each song
  has now, so what is on the files already is not read as an answer. A familiarity on a song with
  nothing in `Tagged` is left alone, since an earlier lookup may have written it.
- **Lead or Sparse** is recorded per stem file (`VocalAnswers`, by track and preset), and
  `Type|Vocal|Lead` / `Type|Vocal|Sparse` are keywords crateside accepts, so the stems tagged by hand
  on 2026-11-09 read as answers and take crateside's other tags. crateside writes none yet, so only
  an answer given or taken off in Live is recorded; the rejection of a value crateside wrote waits
  for [#195](https://github.com/Bamo16/crateside/issues/195).
