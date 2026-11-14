# 0092 - a Meter tag set in Live records that the track was heard through

- Status: Accepted
- Date: 2026-11-11
- Built by [#154](https://github.com/Bamo16/crateside/issues/154).
- Odd bars follows the tags; the hand mark of [ADR 0058](0058-a-staged-stem-shows-its-state-in-live.md)
  gives way once a track has one. The starter's outcome is [ADR 0075](0075-the-dashboard-shows-how-the-starter-warps-fared.md)'s.

## Context

Moving a track's stems from Staging to the library is the maintainer signing off on its warp, and
often on having heard the whole track, but nothing recorded it. A track without the Odd bars mark was
only presumed to be 4/4, so a beatdetect measurement could claim a correct downbeat at the clip start
and no further.

## Decision

- **`Meter|<value>` is a tag group the maintainer sets in Live.** A base is any `n/d` or `Mixed`;
  `Odd bars` is the modifier. crateside accepts any `n/d`, writes none of its own, and shows a track's
  Meter on its page without editing it. **A Meter tag means the maintainer heard the track through
  and signed off its meter; no Meter tag means unchecked.**
- **All stems of a track agree.** crateside keeps, per song, the set it last applied
  (`%APPDATA%\crateside\meters.json`, `MeterStore`). On each backing pass a value on a stem that is
  not in the set is added to it, and a value in the set that the person took off a stem (Live's
  hidden keyword) is removed from it, once per stem and value. The result is written to every stem
  of the track in Staging and the library, and a value taken out is taken off the stems that still
  hold it.
- **A contradiction is not copied.** Two bases, or `Mixed` beside an `n/d`, or different changes on
  different stems leave the set as it was and mark the song as contradicting: the track page says so
  and Odd bars shows on its stems.
- **Odd bars is computed.** Once a song has Meter tags the Collection shows for anything but plain
  `4/4`, and for a contradiction; plain `4/4` takes it off whatever the hand mark says. A song with
  no Meter tags keeps its hand mark, and the track page keeps the mark's button until then.
- **The starter outcome is not stored.** ADR 0075 already compares the starter with the finished
  warp from the reading and the warp's beat line, so it needs no record per track. The Dashboard
  panel now says how many kept starters also carry a Meter tag: only those are known to be right
  throughout.

## Consequences

- crateside's tag writes keep every keyword they do not own, `Meter` included (tested); an entry
  holding a keyword the writer does not know is left unwritten, as before.
- A Meter value removed from a stem and put back on it later is written to the other stems, but not
  back onto the stem it was removed from, because crateside never writes a keyword the person hid.
- The record is keyed by song and is not carried across a song's re-judging as the ledger-kept marks
  are; a song re-keyed after its first Meter tag is read again from its stems.
- Unmeasured on the maintainer's library: no stem carried a Meter tag when this was built.
