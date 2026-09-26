# What the `.asd` sidecar records, and when

*Note, 2026-09-26, frozen. Measured with the user in Ableton, and across the real library. For
[#67](https://github.com/Bamo16/crateside/issues/67).*

## The experiment

One stem, `Becky Hill, Chase & Status - Disconnect (Instrumental - Full).flac`, in Sample Staging.
The user warped its three stems as a group, as usual, and the sidecar was copied after each step:

| step | what the user did | the `.asd` |
|---|---|---|
| 1 | selected it in the browser, which rendered a preview | written, 14:11:06, 496,933 bytes |
| 2 | pressed **Warp** | **byte-identical to 1** |
| 3 | warped it properly: one marker on the first snare (beat 2 of bar 40), the default marker deleted, tempo dragged to 175.00 | **byte-identical to 1** |
| 4 | set the start at bar 9 (verse 1) and pressed **Save Default Clip** | rewritten, 14:30:39, 509,038 bytes, **and the other two stems' sidecars in the same second** (the Vocal had none until then) |

**Nothing about warping reaches the sidecar until Save Default Clip.** Step 4 changed:

| field | 1–3 | 4 |
|---|---|---|
| `WarpMarkers` | none | one marker, 54.1703 s = beat 157 (plus Ableton's 1/32-beat handle), tempo **175.0007** |
| `LoopStart` | 0 | **32** (beats: bar 9, the default clip's start) |
| `LoopEnd`, `HiddenLoopEnd` | 0 | 478.263 |
| `OutMarker` | 0 | 1386.9627 (units not established) |
| `HiQ` | false | true |
| `ColorIndex` | -1 | 10 |
| `LaunchQuantisation` | 256 | 0 |
| `IsWarped` | **true** | true |

`IsWarped` is true on a sidecar nobody has warped, so it says nothing.

## Across the library, the same day

- **Mashup Samples, 843 sidecars:** 842 carry the saved shape: markers, `LoopEnd` set, `LoopStart` in
  beats and on a bar line in 819 (32: 160, 64: 90, 16: 73, 48: 59, 0: 50…). The one exception has the
  untouched shape: `Kreayshawn - Gucci Gucci (Acapella).flac`.
- **Sample Staging** held two untouched sidecars before the experiment.
- **One warp marker is the norm:** 704 of the library's 842 warped files are a single user-placed
  marker, stored as a pair 1/32 of a beat apart. Only 8 sit at 0 s; the rest are on a real beat deep
  into the song. 714 of 842 hold one steady tempo; 106 have 21+ markers (the hard warps).

## What follows

- **A sidecar with warp markers (or a non-zero `LoopEnd`) is a finished warp with its default clip
  saved.** A sidecar without them is unwarped, or a warp still in progress: the two are
  indistinguishable on disk.
- **The warped tempo is ground truth.** The user sets it by ear against the grid and can defend it
  (175.00 here). The one case they would be less sure of is a track with no drums and a changing
  tempo. Against it, on 332 songs: keytag's recommended BPM was within 0.5 on 205 and wrong on 61;
  raw `beatdetect` was within 0.5 on 124 and wrong on 129, mostly at about 2/3 or 3/4 of the tempo
  (a triplet or dotted subdivision). For drum and bass the user starts from 174 rather than any
  detector, because detectors fail most on it.

## How the fields were read

The `.asd` carries its own type schema (`SampleData`, then nested classes, field names in UTF-16).
Every fixed-width field before `WarpMarkers` has a computable offset, which `AbletonWarpReader`
already walks. The fields after the variable-length marker list (`MarkersGenerated`, `LaunchMode`,
`LoopOn`, `LaunchQuantisation`) follow the 32-byte marker records directly.
