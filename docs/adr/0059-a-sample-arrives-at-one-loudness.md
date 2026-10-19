# 0059 — A sample arrives at one loudness

- Status: Accepted
- Date: 2026-10-19
- Amends [ADR 0058](0058-a-staged-stem-shows-its-state-in-live.md): crateside writes
  `SampleVolume` as well as `ColorIndex` on a sidecar it did not write, and does so in the library
  as well as in staging. Builds [#131](https://github.com/Bamo16/crateside/issues/131).

## Context

Two samples dragged into an arrangement rarely match in level, and the maintainer sets a clip gain
that the next pairing undoes. The measurement
([note, 2026-10-19](../notes/2026-10-19-loudness-spread-of-the-library.md), MEASURED over 1,112
files) found the spread real: 49% of samples within ±1.5 dB of the median, the 5th to 95th
percentile 9.5 dB wide, vocals a median 2.0 dB below their own instrumental. What hurts in use is
the extremes, where one part buries the other.

## Decision

**A sample's default clip carries a `SampleVolume` that brings its whole file to one target
integrated loudness.** The audio is never touched; crateside writes the one field.

- **Metric: integrated loudness (EBU R128), whole file.** Eight vocal/instrumental pairs were
  rendered under integrated and under the 95th percentile of short-term loudness and listened to
  by the maintainer, who could not tell them apart. Where the ear is indifferent, integrated is
  the one ffmpeg already reports.
- **Target: -14 LUFS.** A GUESS, near the library median (-13.8), so most files move little and
  the extremes move most. The target is a setting, not a constant: the maintainer chose it
  without a reference, and changing it re-writes every sidecar.
- **Stems of a song stop summing to the source's balance**, accepted in #131. No per-kind offset
  to start with.
- **Drums are not levelled.** They are a warping guide the maintainer rarely hears; their gain
  stays 0.
- **A stem below -24 LUFS integrated is not levelled either.** It is separator residue, such as
  the vocal stem of an instrumental that was queued with the default recipe, and a gain would
  amplify it. The floor is a GUESS from the distribution (7 of 513 vocal stems are below it, and
  the one listened to, fusq's *Perfume!*, an instrumental, sat near -27); it is a setting.
- **crateside owns the gain of every sidecar with a saved default clip**, in the library and in
  staging, the maintainer's hand-set gains included, and re-writes it whenever the file is off
  target, including after Live saves over it. A sidecar that is with Live (#97) is left until
  Live is done. Starter and lent warps carry the gain from the first write.
- **A stem with no starter** gets a sidecar holding its colour and gain and no warp, recorded as
  crateside's own (as starters are) so the board does not read an unwarped stem as warped (ADR
  0037). Whether Live applies it and keeps the manual warp flow is a spike, not yet run.
- **The loudness reading lives in `Crateside.Core`**, not AbleKit, and is shared with #39.

## Consequences

- **Every gain is one number away from undone.** Setting each saved default clip's `SampleVolume`
  to 1 (0 dB) reverts the change, and the target setting retunes it.
- **Still unmeasured:** that Live applies a gain crateside wrote on drag (observed only for gains
  saved through Live's *Save Default Clip*), and the colour-and-gain sidecar with no warp. True
  peak was not measured: clip gain above 0 dBFS is routine, and only the Main track, which carries
  a limiter, can clip.
- **A quiet vocal is lifted by at most about 10 dB** with the floor in place; the boost is capped
  by the floor, not by a separate limit.
