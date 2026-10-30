# beatdetect: weighted mixes, pooled parts, and telling an unsteady tempo

*Note, 2026-09-28, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68). Measured
on this machine with FiLL's reworked `beatdetect` (`3f1b218`), CPU only. Follows
[the beat-grid note](2026-09-28-beatdetect-beat-grid-against-the-warps.md) of the same day; the
first two parts use its 23 songs and comparison.*

Three things tried before asking FiLL for a filter that takes several drum stems at once.

## Weighted mixes of the parts

DrumSep's kick, snare and hi-hat summed with ffmpeg's `amix` (weights as given, normalized) ahead of
`beatdetect`. Whole files; the window behaves the same way.

| input | exact | ½ or ×2 | other | grid on the beat | within 10 ms |
|---|---|---|---|---|---|
| drum stem | 16 | 4 | 3 | 17 | 4 |
| kick + snare | 14 | 4 | 5 | 15 | 4 |
| kick + snare + ½ hi-hat | 14 | 4 | 5 | 15 | 4 |
| kick + snare + hi-hat | 15 | 4 | 4 | 16 | 5 |
| **kick + 2 × snare + ½ hi-hat** | 13 | 9 | 1 | **20** | **7** |

- **Only the snare-heavy mix beats the drum stem**: the tempo up to the octave on 22 of 23 and the
  grid on the beat on 20, including aespa – *Spicy*'s whole file (+258 ms → +5 ms). It reads half tempo more
  often (9 against 4), which the user treats as their call (ADR 0038).
- Equal weights do no better than the drum stem, which is close to an equal mix already.
- The one-hop lateness is unchanged in every mix (median +12 to +16 ms at the marker, whole file).

## Pooling the parts' tempo candidates

At verbose level the filter logs each tempo candidate with its grid fit. Run on the drum stem, kick,
snare and hi-hat, every candidate's fit is summed into its tempo class folded to one octave; the
winning class takes its octave from the drum stem's candidates.

- **Tempo up to the octave: 22 of 23, against the drum stem's 20.** The last one, NXONE'S FAVOURITE,
  Nakedleisure – *NOT A DREAM*, is
  the right class read at a quarter (41.76 against 167).
- **It fixes Lil Wayne, Static Major – *Lollipop*** (warped 148): the drum stem's best grid fit was 148.05 (0.138), but the
  filter chose 98.66 (0.133) on a near tie that goes to the stronger comb response; the snare and
  hi-hat both put 74.01 first, and the pool picks 148.05.
- Sarah Cothran – *As the World Caves In* moves from 206.23 to 61.94 (half) on a close vote (0.124 against 0.115).
- Pooling decides tempo only: the log gives no phase per candidate.

## Telling an unsteady tempo

What is wanted from an inconsistent track is only *that* it is inconsistent. Measured on all
429 warped library instrumentals (no drum stems), against crateside's shape from the warp. 418 could
be scored (three or more windows in one octave): 361 steady, 22 steady with changes, 35 unsteady.

**The filter's own figures do not tell them apart.** Confidence, grid fit, separation and stability
overlap across the three shapes. Stability counts rolling detections within 4 % of the final tempo,
and an unsteady track drifts less than that (Jackson 5 – *I Want You Back*: 95.98–99.93, stability
  0.91). The
spread of the per-frame running estimate is better but flags 88 steady tracks to catch 24 unsteady.

**Consecutive 30 s windows do.** The filter run on each 30 s of the file, each window's tempo from
`beat_interval_ms`, and the spread of the windows read in the majority octave (octave-flipped readings
are not exact multiples, and would add a spread of their own). Interquartile range, as a percentage
of the median:

| flag at | steady | with changes | unsteady |
|---|---|---|---|
| ≥ 0.05 % | 35 of 361 | 11 of 22 | 34 of 35 |
| ≥ 0.1 % | 11 of 361 | 7 of 22 | 28 of 35 |
| ≥ 0.2 % | 6 of 361 | 3 of 22 | 25 of 35 |

- **A steady track's windows agree to a few hundredths**: Rob Zombie – *Superbeast*'s seven read
  153.98–154.03. *I Want You Back*'s five read 97.16–98.47 (interquartile range 0.23 %).
- **What it misses is jitter around a steady mean.** Cypress Hill – *Hits from the Bong* is warped
  with 168 markers across 90.78–93.54, yet every window reads 92.15–92.16: this test sees drift, not bar-to-bar
  variation that averages out within 30 s. The other six missed at 0.1 % sit just under it (0.05–0.10 %).
- **Its steady false alarms** at 0.1 % include windows misread outright
  (NLE Choppa – *Or What*, Ludacris – *How Low*) and tracks with sparse or drumless stretches
  (WALK THE MOON – *Anna Sun*, Sarah Cothran – *As the World Caves In*). Eminem – *My Name Is*
  (warped 85.52, one marker) reads 85.28–85.78 across windows and may drift.
- **The range between the lowest and highest window is worse** than the interquartile range: one bad
  intro or outro window is enough.
- This needs the reworked build. The pre-rework filter on PATH reports tempo on a grid about 0.8 BPM
  apart, coarser than the spreads being measured.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-28 beat grid\`: `mixes.tsv`, `combine.tsv`.
`%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-28 steadiness\`: `shapes.tsv` (each instrumental's warp
shape), `steadiness.tsv` (the filter's own figures), `windows.tsv` and `windows-octave.tsv`. Scripts in
`BPM Corpus\scripts`: `mixes.py`, `combine.py`, `shapes.cs`, `steadiness.py`, `windows.py`,
`rescore_windows.py`.
