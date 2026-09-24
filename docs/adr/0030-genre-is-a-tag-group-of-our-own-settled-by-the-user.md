# 0030 — genre is a tag group of our own, settled by the user

- Status: Accepted
- Date: 2026-09-24
- Settles [#2](https://github.com/Bamo16/crateside/issues/2), [#4](https://github.com/Bamo16/crateside/issues/4),
  [#5](https://github.com/Bamo16/crateside/issues/5) and [#6](https://github.com/Bamo16/crateside/issues/6).
  Amends [ADR 0023](0023-a-key-with-no-store-entry-is-not-durable.md).

## Context

The user wants to filter samples by genre in Ableton's browser. The stated use is speed, not
classification: *"something cute to combine with an aggressive hip hop vocal"*, or a rapped vocal,
which works over nearly any key.

Measured 2026-09-24, on a random 30 songs from the ledger (comment on #4):

- **The download carries no genre.** yt-dlp's metadata had none for any of the 30.
- **iTunes answered 27 and Deezer 25**, one coarse genre each. Both call Fox Stevenson's drum and
  bass `Dance`, and one J/K-pop act `Hip-Hop/Rap`.
- **MusicBrainz answered 19**, with community tags fine enough to say `drum and bass`.

Ableton's built-in *Genres* group has 31 tags, most of which the user will never use. Every one of
them shows when the group is expanded in the browser.

## Decision

**A tag group of crateside's own, `Genre`, holding a short closed list. crateside suggests; the
user settles; only a settled genre is written.**

- **The list:** `Drum & Bass`, `Future Bass`, `Hip Hop`, `J-Pop`, `K-Pop`, `Pop`, `Rock`, in
  Ableton's spelling. It lives in `GenreVocabulary`, and the writer refuses any other `Genre|`
  keyword. It grows only by a code change, and **slowly**: the user wants to see what the tag is
  worth in practice before widening it.
- **The group is crateside's own**, so the browser shows only the tags in use. crateside writes the
  first `Genre|` keyword itself, and Ableton makes the group from it (ADR 0003 finding 8, working
  for us). **Unmeasured** until that first write: that Ableton makes a *group* from an unknown
  prefix the way it makes a tag.
- **Suggested by lookup.** `GenreWorker` asks MusicBrainz (recording, then artist) and iTunes for
  every song that has no record, one at a time, unattended; it contends for nothing and writes no
  tag. The first label that maps onto the list is the suggestion.
- **Settled on the row.** The expanded row shows the list with the suggestion marked; a click
  settles it, and *none of these* is an answer too.
- **Keyed by song identity** in `%APPDATA%\crateside\genres.json`, as the ledger keys a verdict.
  Backing reads a file's provenance, then its song, then its settled genre, for as long as the
  file exists. That is what keeps a genre on a file moved into the library (the trap ADR 0028
  describes for Type). A song the ledger never judged is keyed by its upload.
- **Written by the backing pass**, over both roots, into entries that already carry a key
  (ADR 0023's amendment).
- **Everything the sources said is kept**, mapped or not. The Library page lists the labels of
  songs the list does not cover, most common first. A label that keeps coming up is the signal that
  the list wants another genre, which is how "extend it when it is needed" becomes visible.

## Considered options

- **Ableton's own *Genres* group.** It needs no new group, but the browser would show all 31 tags.
- **The judging agent.** Better than a storefront at drum and bass against dance, but it spends
  quota on every track, and does nothing for the ~800 files already in the library.
- **A genre in the file's comment, as Type is (ADR 0028).** That would mean rewriting the audio tags
  of every library file, warped ones included, for a fact that belongs to the song rather than the
  file. A store keyed by song needs one write per song.

## Consequences

- Every song in the library needs settling by hand before it filters well: 313 judged songs and
  90 never-judged uploads, as #47 counted them on 2026-09-22. The suggestion makes each one a
  single click, but that is still around four hundred clicks. A bulk *accept the suggestions* is
  not built.
- The ~60 library files with no source URL in their provenance (#45, #47) cannot be joined to a
  song, so they get no genre until they are claimed.
- Ableton's index, which the Library page counts from, follows a store write by minutes to hours
  (ADR 0013), so a settled genre shows up in the count late.
- A lookup can go wrong, as a storefront did in the survey by returning a karaoke cover. Nothing is
  written until the user settles it, so a wrong suggestion costs a click, not a wrong tag.
