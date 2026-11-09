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
