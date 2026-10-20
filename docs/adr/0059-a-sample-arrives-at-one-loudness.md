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
- **A stem with no starter** gets a sidecar holding its colour and gain with **Warp off** and the
  clip's start and end in seconds over the whole file, recorded as crateside's own (as starters
  are) so the board does not read an unwarped stem as warped (ADR 0037). MEASURED 2026-10-19 in
  Live: such a clip came in at -6 dB in colour 14 (red), unwarped and playing at the source's tempo,
  and Warp switched on as it does for a sample never warped (one marker at the start, the set's
  tempo). The same clip with Warp **on** and no markers was ignored whole, gain and colour, at
  zero and at the clip's end in beats alike; a guess is that Live checks the sidecar and rejects a
  warped clip with no markers, which Live's own editor cannot make.
- **The loudness reading lives in `Crateside.Core`**, not AbleKit, and is shared with #39.

**Amended 2026-10-19, as built:**

- **"Never levelled" means 0 dB, enforced.** A drums stem or one below the floor is given a gain of
  1, so a hand-set gain on either is discarded like any other.
- **On target is within 0.05 dB** of the gain wanted, so float rounding never rewrites a sidecar.
- **The library gets the gain, not the colour.** ADR 0058's colour stays staging's; only
  `SampleVolume` is written on a library sidecar.
- **The Warp-off clip follows the starter-warp setting.** It is written where a starter would be,
  when the stem gets none (its tempo drifts or is not read yet), and not at all with starter warps
  *Off*, which leaves the sidecar for Live. A starter or a lent warp replaces it, never the other
  way round, and it never lends. (Amended by [ADR 0060](0060-a-starter-is-written-only-where-one-tempo-holds.md):
  it now replaces a starter whose tempo does not hold.)
- **A file ffmpeg cannot read keeps its gain**, and a starter or lent warp written for one carries
  0 dB, not the lender's.
- **Every sample is read**, saved clip or not, once a minute for any without a current reading,
  eight at a time, so a clip saved later is levelled within the minute.

## Consequences

- **Every gain is one number away from undone.** Setting each saved default clip's `SampleVolume`
  to 1 (0 dB) reverts the change, and the target setting retunes it.
- **MEASURED 2026-10-19:** a gain written into a sidecar by AbleKit applies on drag (-6 and +6 dB,
  on a starter-shaped clip), and so does a colour-and-gain sidecar with Warp off (above).
- **Not measured:** true
  peak was not measured: clip gain above 0 dBFS is routine, and only the Main track, which carries
  a limiter, can clip.
- **A quiet vocal is lifted by at most about 10 dB** with the floor in place; the boost is capped
  by the floor, not by a separate limit.
