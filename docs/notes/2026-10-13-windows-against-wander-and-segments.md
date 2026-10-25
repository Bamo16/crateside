# The 30 s windows against beatdetect's own wander and segments

*Note, 2026-10-13, frozen. For [#106](https://github.com/Bamo16/crateside/issues/106); decides
nothing itself. Measured on this machine with the pinned 1.3.0 build (`1.3.0-1e57fec`), CPU only,
whole files, one run per track. The windows' side is
[#75's note](2026-09-28-beatdetect-mixes-pooled-parts-and-unsteady-tempo.md), not re-run.*

## What was counted

- **Tracks:** the 429 instrumentals of #75's `shapes.tsv`, each with the shape its warp has
  (steady / steady with changes / unsteady). 427 read; two share a song name in the file and
  collapse to one reading each (*measured*).
- **Scored set:** the 416 whose windows #75's own `windows-octave.tsv` could score (three or more
  windows in one octave): **359 steady, 22 with changes, 35 unsteady**. #75's text says 361 steady;
  the file has 359. The two-track difference is unexplained, and the rule's "361" is read as 359
  here (*measured; cause unknown*).
- **Windows' flag:** `iqr2 >= 0.1 %` from that same file, i.e. #75's own numbers (*measured then*).
- **Wander and segments:** `tempo_wander` from the last frame and the count of `tempo_segmentN`
  lines in the last frame, both from the whole-file run.

## Result

Caught of unsteady (35), falsely flagged of steady (359), and of steady-with-changes (22).

| flag | unsteady | steady | with changes |
|---|---|---|---|
| windows, spread ≥ 0.1 % (#75) | 28 | 11 | 7 |
| wander ≥ 0.5 | 27 | 45 | 12 |
| wander ≥ 0.7 | 26 | 34 | 11 |
| wander ≥ 1.0 | 25 | 27 | 10 |
| segments > 1 | 27 | 19 | 9 |
| wander ≥ 0.7 or segments > 1 | 28 | 38 | 12 |
| wander ≥ 1.0 or segments > 1 | 28 | 32 | 11 |

Between 0.8 and 1.3 wander catches 26 / 26 / 25 / 22 / 22 / 21 and falsely flags 29 / 29 / 27 / 27 /
26 / 25: no wander cut-off gets under 25 false alarms while catching 25.

**Against the committed rule** (at least 25 of 35 caught, at most 15 of 361 falsely flagged, at one
W): none of the flags the issue named passes. The closest, `segments > 1`, catches 27 and falsely
flags 19; `wander ≥ 1.0` catches 25 and falsely flags 27.

## A flag the rule did not name

Requiring both, `segments > 1 and wander ≥ W`, catches 26 / 25 / 24 of the unsteady and falsely
flags 15 / 15 / 14 steady at W = 0.5 / 0.7 / 1.0. At 0.5 or 0.7 that is on the rule's numbers. It
was found by sweeping after the named flags had failed, and it sits on the limit (15 of 359): so a
fit to this set, *guessed* to generalize and not tested outside it.

## Other findings

- **Same tracks, different misses.** Segments catch 2 unsteady warps the windows miss, and miss 3
  the windows catch (*measured*). Of the 19 steady tracks segments flag, 16 are not flagged by the
  windows.
- **Segment counts:** 340 of 359 steady tracks have one segment (10 have two); the unsteady ones
  range from 1 (8 tracks) to 15.
- **Wander:** median 0.155 for steady, 1.58 for unsteady; the 90th percentile is 0.69 and 5.2.
- **The unwarped-tempo fallback** (whole-file `bpm` against the warp, steady warps only, 359):
  277 within 1 %; 351 within 1 % after folding by octaves; 8 in another class (*Lollipop*,
  *How Low*, *Or What*, *Number One Spot*, *Kitty – Morgan Stop*, *Stuntin' Like My Daddy*, *A Horny
  Money World*, *Lose Control*). The issue's "18 of 21 within 0.01" could not be matched to a
  definition here; these figures are on the whole steady set.
- *Get Low* reads 101.0007 against a warp of 100.985, wander 0.08, one segment, under this build.
  *Hello* is not in #75's tracks, so it is not measured here.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-10-13 wander\readings.jsonl` (one line per track);
scripts `scripts\wander_run.py` and `scripts\wander_score.py`.
