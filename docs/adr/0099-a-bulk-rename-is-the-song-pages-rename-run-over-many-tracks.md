# 0099 - a bulk rename is the song page's rename run over many tracks

- Status: Accepted
- Date: 2026-11-17
- Issue [#202](https://github.com/Bamo16/crateside/issues/202).

## Context

Naming tracks from their tags (album, game, artist, title) meant opening each song page. Bamo
wanted a pattern applied to many tracks at once, with a preview.

## Decision

- **The Tracks page offers "Rename from tags"** on the tracks it shows (filtered by album if wanted).
  A pattern (`{album} - {title}`, `{artist} - {title}`, `{game} - {title}`, or his own) gives a
  preview of every new name; Apply renames.
- **Every rename goes through `TrackRename`**, the song page's path, so stems, the source copy and
  the Ableton sets follow and a track in an open set is held back. Each rename is logged as
  `Renamed`.
- **`{game}` is the album before its colon**: `Donkey Kong Country 2: Diddy's Kong Quest` is
  `Donkey Kong Country 2`.
- **Names come from tags, never from the renamed name**: `{artist}` and `{title}` read the intake
  tags (`TrackRow.TagArtist`, `TagTitle`), so a rename cannot change what lookups search for.
- **A track gets no new name, with the reason shown**, when a token has no value, the name is
  unusable, or another track in the batch would get the same name. A track already named by the
  pattern is left alone.
- **Inbox folders rename nothing by themselves.**
