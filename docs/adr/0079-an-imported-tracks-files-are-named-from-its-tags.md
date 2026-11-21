# 0079 - an imported track's files are named from its tags

- Status: Accepted
- Date: 2026-11-07
- Corrects [ADR 0077](0077-a-track-that-is-not-on-youtube-is-keyed-by-its-source-and-named-from-its-tags.md)
  (the name a track is shown by); uses the rename of [ADR 0039](0039-crateside-renames-in-the-library-and-warns-about-sets.md)
  where it exists.

## Context

An imported track is shown as `Yoko Shimomura - Fight Against Koopa` until it is separated. Then
the row read `1-05 Fight Against Koopa`: the row prefers the name its staged files carry, and
StemForge names stems, and crateside names the kept copy of the source, after the imported file,
which keeps the name it arrived with (`imports\<key>\1-05 Fight Against Koopa.flac`). Measured on
24 imports of a soundtrack, 2026-11-07.

## Decision

**The handoff names the files.** For a track that is not a video ID, `HandoffDrainer` gives the
runner the track's tag-built name (the `DisplayTitle` intake recorded, with characters a file name
cannot hold replaced by `_`), unless the person has named the track, whose name wins as before. A
YouTube track is unchanged: its download already carries the name the separator was asked for.

The alternative was to name the imported copy at intake. It was not chosen because the handoff is
the one place every file of a track passes (the stems and the kept source copy, which is written
beside the stems), it covers imports taken in before this change that are not yet separated, and
the imported file keeps the name it came with. A person's rename still lands in the same place.

**Imports already staged or in the library under the file name are renamed by the existing rename.**
`ImportNameRepair` runs from `ImportNameWorker` 45 seconds after start and every ten minutes,
and calls `TrackRename.Revert` with the tag-built name for each non-YouTube track that has no
name of the person's: the staged files, Ableton's tag store and the sets that use them follow, as
for a person's Rename, and a refusal (an open set that uses the files) is tried again later.

## Consequences

- A track the person has renamed is never touched.
- A name with `/` or `:` in a tag shows `_` in its files; the row shows the files' name.
