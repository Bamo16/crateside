# How precise crateside's beatdetect reading is, against the warps

*Note, 2026-10-02, frozen. For the BPM display rule and starter warps (#77).*

The question: before anyone warps a track, can crateside know its tempo to the hundredth of a BPM?
That matters twice. Two decimals on screen should mean the hundredths are known, and a one-marker
starter warp is only right if its tempo is: at 128 BPM, 0.02 BPM off drifts about 37 ms by the end
of a four-minute track.

## What was compared

- **Ground truth:** every steady warp's tempo, from crateside's warp tempo cache (`tempos.json`,
  shape `Steady`).
- **The reading:** crateside's beatdetect reading of the same instrumental (`beats.json`, ffmpeg
  9.0-NoMercy-beatdetect-v1.1.0, hpss): the whole file's figure, and each 30 s window's.
- 437 instrumentals have both. Readings in a different octave were folded to the warp's. The five
  at a triplet or dotted ratio (Missy Elliott, Ciara, Fatman Scoop – *Lose Control* reads 167.27
  against 125.46) are left out of the precision figures, leaving 432.

## Results (measured)

| | within 0.005 | within 0.01 | within 0.02 | within 0.05 |
|---|---|---|---|---|
| whole-file reading, all 432 | 353 | 407 | 427 | 430 |
| median of the 30 s windows | 118 | 237 | 371 | 424 |
| whole-file reading, the 386 crateside calls steady (window spread under 0.1%) | 319 | 366 | 382 | 385 |

- **The whole-file figure is the precise one.** The windows are for telling steady from drifting,
  not for the value.
- **Hundredths are common in the warps:** 132 of the 437 steady warps do not round to a whole BPM at
  two decimals. Examples: Mr Oizo – *Jo* 119.50, Eminem – *My Name Is* 85.52, The Notorious B.I.G. –
  *Big Poppa* 84.52, *Cruel Summer* 108.51, Sir Mix-A-Lot – *Baby Got Back* 128.50. The readings matched
  each of those to the hundredth.
- **A warp is not an exact whole number either.** Flo Rida – *Low* is warped at 128.002. A displayed
  tempo has to round to two decimals before deciding it is whole.
- **The window spread predicts a precise reading.** Of the 304 with a spread at or under 0.02%,
  296 read within 0.01 of the warp and 303 within 0.02. Of the 25 misses beyond 0.01, the median
  spread was 0.033%, against 0.0115% for the hits. Confidence did not tell them apart (0.305 against
  0.311).
- **Snapping a near-whole reading to the whole number** (within 0.02 or 0.05) changed the count
  within 0.01 by at most one, so a starter can use the reading as it is.
