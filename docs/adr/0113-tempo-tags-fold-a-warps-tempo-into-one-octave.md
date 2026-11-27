# 0113 - Tempo tags fold a warp's tempo into one octave

- Status: Accepted
- Date: 2026-11-26
- Builds [#242](https://github.com/Bamo16/crateside/issues/242). Follows
  [ADR 0112](0112-camelot-tags-are-derived-from-the-key-tags.md) (a group crateside derives on every
  write, overwritten when edited in Live) and reads the tempo of
  [ADR 0038](0038-a-warps-bpm-is-the-stable-tempo-that-holds-longest.md)'s `WarpTempo`. Decided by the
  maintainer on 2026-11-26.

## Context

Live has no tempo tag, and tags are exact strings, so the browser cannot find "stems near this
tempo". Stems sit together when they will share a tempo once the clip's ×2 and ÷2 buttons have
fixed an octave: 100 and 198 BPM are closer than 100 and 95.

## Decision

- **A group of crateside's own, `Tempo`, with 12 tags**, each named for its centre and its double:
  `Tempo|≈87 · 174`, so a DnB track's tag reads as its own tempo too.
- **The tempo is the one the warp holds longest** (`WarpTempo.Bpm`: the stable tempo with the most
  audible bars, or the median bar when none is stable). A file whose tempo changes gets that one
  tempo only. A file with no warp, a starter warp or a Warp-off clip has no `WarpTempo`, and
  ends with no Tempo tag.
- **Folded into 80-160 BPM** by halving or doubling (174 becomes 87, 60 becomes 120); 3:2 is not
  folded. Only the tag folds: the clips keep their real tempo.
- **12 bands per octave**, each about 6 % wide (one semitone of stretch), centres
  `80 * 2^((k + 0.5) / 12)`: about 82, 87, 92, 98, 104, 110, 116, 123, 131, 138, 147, 155.
  `TempoTags.BandsPerOctave` is the one constant; changing it renames the tags on the next backing
  pass.
- **Every file carries its two nearest bands.** Live ANDs tags within a group, so two neighbouring
  bands cannot be selected at once; with two bands per file, one tag finds everything within about
  6 % of its centre and no edge splits 99.9 from 100.1. The octave is a circle: 158 BPM takes the
  155 band and the 82 one.
- **Derived on every write, like the Camelot tags.** `TempoTags.Reconcile` replaces every `Tempo|`
  keyword in the bag a backing pass is about to store, on both roots, from
  `WarpTempoReader.Read`; a bag with no whole key is left alone, so a keyless entry is never
  written for its tempo. A hand edit in Live is overwritten the next time the file is written, and
  a tempo tag on a file whose warp is gone is taken off.
- **The tagging stage does not write it**: a stem freshly cut into Staging has no warp yet, so its
  tempo tags arrive with the first backing pass after the warp is saved.
- **Backfill is the backing pass**, with no button. The keyword is known to the writer
  (`CratesideKeywords.IsKnown`), so a file carrying Tempo tags is not left alone as holding one
  the writer would refuse.

## Considered options

- **One tag per file.** A file at 99.9 BPM and one at 100.1 would land in different tags, with no
  way to select both.
- **Narrower bands, or fewer.** Left to use: the band count is one constant, and the maintainer may
  want fewer after seeing the browser (or a setting).

## Consequences

- A warp that drifts is tagged by its median tempo, which may be nothing the track plays for long;
  revisit if it bites.
- A vocal pitched up 1.5x or more to fit a faster instrumental is outside a 6 % band; that search
  wants its own design.
- Reading every sidecar's tempo on the first pass costs what the Tracks page's first read does
  (ADR 0038), after which the kept cache answers.
