# 0091 - a saved clip with Warp off carries the marker pair Live writes

- Status: Accepted
- Date: 2026-11-10
- Follows [ADR 0059](0059-a-sample-arrives-at-one-loudness.md), which introduced the Warp off clip.
- Tracked by [#158](https://github.com/Bamo16/crateside/issues/158).

## Context

A Warp off clip (ADR 0059) was written with no warp markers. **Confirmed on 2026-11-10 in Live
12.4.7 on Windows:** selecting a stem with such a sidecar in the browser crashes Live, which reads
through a null object while walking the marker list. It first crashed seven times on 2026-10-25.

Live itself always writes a pair of markers for a saved clip, even with Warp off, with
`MarkersGenerated` set: one at the clip start in seconds at beat 0, and one 1/32 beat later at the
set's tempo. Adding that pair to a crashing sidecar, and nothing else, loaded without a crash and
kept the saved clip (+6 dB gain shown). Turning Warp on later re-spaces the pair to the arrangement's
tempo, so the spacing barely matters. The evidence is in
`%LOCALAPPDATA%\crateside\diagnostics\2026-11-10-flashdance-crash\`.

97 of 203 sidecars in the staging folder had the shape; none in the mashup samples.

## Decision

- **A saved clip is never written with fewer than two warp markers.** AbleKit 0.1.0-preview.4
  refuses to write one and offers `Warp.Off(clipStart, tempo)`, which builds Live's pair.
- **The Warp off clip carries that pair at its clip start (0), spaced at the track's estimated
  tempo when crateside has one, else 120.** The markers say nothing of the user's tempo, so
  nothing reads a tempo, a clip range or a loop through them while Warp is off.
- **A one-time repair gives every existing sidecar that has the shape the pair.** It runs in the
  app after start, backs each sidecar up first (`%APPDATA%\crateside\backups\warps\`), rewrites only
  the marker list, and leaves a file an open set uses, or one waiting for Live to analyze it, until
  that is over. It logs each sample it repaired and how many it left. Once nothing is left it stops.
- **Until a file is repaired, no other writer touches it.** The clip color and gain passes skip a
  sidecar AbleKit refuses, instead of failing the pass.

## Consequences

- The repair is idempotent: the shape is the only thing it looks for, so a file Live saved over, or
  one that already has markers, is never touched.
- The marker pair is the only change to the file. `MarkersGenerated` stays as it was.
