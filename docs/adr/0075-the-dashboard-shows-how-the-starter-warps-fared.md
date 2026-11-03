# 0075 - The Dashboard shows how the starter warps fared, not the BPM detectors

- Status: Accepted
- Date: 2026-11-03
- Replaces the Dashboard panel of [ADR 0041](0041-crateside-measures-bpm-detectors-against-the-warps.md);
  its rule that a finished warp's tempo is the truth stands.

## Context

*BPM detectors against the warps* read as if several detectors were compared, when only beatdetect is
used, and the maintainer found it of no use. What he wants to know is whether the starter warp
([ADR 0054](0054-a-starter-warps-marker-sits-on-a-beat-the-loud-transients-place.md), bars numbered as
in [ADR 0072](0072-bars-are-numbered-as-lives-ruler-numbers-them.md)) saves him work.

`written-warps.json` holds a starter's marker only for starters written since #176, and Live replaces
a starter's sidecar when it saves: on 2026-11-03 all 6 recorded starters were still unedited, so the
record alone compares nothing yet.

## Decision

- **The panel is *How the starter warps fared*.** It sets the starter crateside would write today,
  taken from beatdetect's cached reading of the song (`StarterGrid`, as the writer takes it), against
  the finished warp's beat line (`WarpTempo.Line`, kept in the tempo cache, version 6). It needs a
  steady finished warp and a reading with a beat; other songs are counted as left out.
- **The starter's marker is the first beat in the file**, the reading's first beat folded into the first beat
  length, as `GridCheck.FirstBeat` places it. This is an approximation: the writer folds crateside's own
  transients and the reading only picks the 16th.
- **Six buckets, each named in plain words** (`StarterFate`), the first that fits:
  - *No starter written*: the reading gives no steady tempo.
  - *Tempo at half or double*: the starter's tempo is 2x or 1/2 the finished warp's (within 1%).
  - *Marker moved*: the tempo differs by more than 1%, or the marker is more than 0.2 beat from a beat
    of the finished warp (beat wrong).
  - *Bar corrected*: the downbeat passed the 0.6 gate and the finished warp numbers the bars
    differently (downbeat wrong).
  - *Left at beat 0*: the downbeat did not pass the gate, so the marker is beat 0, and the finished warp
    numbers it otherwise (low confidence).
  - *Kept as written*: none of those; the marker is on a beat, in the right bar.
- **Each count opens the song names under it.** Tracks has no filter for these, so none is added.

## Consequences

- The first read of the sidecars after this runs once more over the library (the tempo cache's version
  rose), as it did when the grid was added.
- Measured on the maintainer's library on 2026-11-03 (430 instrumentals with a reading and a steady warp): 207 kept, 16 marker
  moved, 84 tempo at half or double, 120 left at beat 0, 0 bar corrected, 3 no starter; 72 non-steady
  warps and 35 unread songs left out. The 120 is far above the 24 gate cases of the 2026-10-31
  downbeat analysis, which looked at the true starter's marker; the approximation above is the likely
  reason, so the *Left at beat 0* count is an upper bound.
- Once starters written since #176 are saved over in Live, their recorded marker could replace the
  approximation.
