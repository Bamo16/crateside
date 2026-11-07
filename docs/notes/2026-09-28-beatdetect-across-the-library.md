# beatdetect across the library, and Ableton's transients as a second reference

*Note, 2026-09-28, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68). Measured on this
machine: both `beatdetect` builds (PATH's pre-rework NoMercy build, and FiLL's `3f1b218`) on every
warped library instrumental, CPU only. The write-up for FiLL is
`%USERPROFILE%\Music\Stems\BPM Corpus\for-fil\beatdetect-on-a-warped-library-2026-09-28.md`.*

## Ground truth, and how far to trust it

429 warped instrumentals: 370 steady (364 single-marker), 23 with changes, 36 unsteady, by
`WarpTempo`. By the user's estimate (owned, not measured), a warp's offset is within 5–10 ms of the
beat, occasionally ~25 ms, and its octave is a judgment, not a fact. So phase is reported in bands
and half or double counts as right.

**Ableton's transients agree with the warps** (measured). `OnSets.Positions` and
`OnSets.TransitionEnergies` from each `.asd`, folded onto the warp's beat period and weighted by
energy: the peak sits a median −1.2 ms from the warp beat, within 10 ms on 297 of 363 and within
25 ms on 327. One file (*Superbeast (Instrumental - Mix)*) was skipped: its FLAC header was not read.

## Tempo, 370 steady warps

| build | span | exact | ½ or ×2 | ×2/3, ×3/4, … | wrong |
|---|---|---|---|---|---|
| pre-rework | whole | 125 | 16 | 29 | 200 |
| pre-rework | window | 157 | 20 | 39 | 154 |
| rework | whole | 281 | 78 | 7 | 4 |
| rework | window | 284 | 74 | 7 | 5 |

- Paired, right up to the octave: whole file 223 fixed, 5 broken; window 187 fixed, 6 broken.
- The rework's precision on exact readings: median 0.025 BPM; within 0.01 on 59 of 281.
- Confidence below 0.12 catches 10 of its 11 whole-file misses and flags 25 of 359 right readings.

## Beat phase, rework, single-marker steady warps with the tempo right up to the octave

| reference | span | n | off the beat | median | within 10 ms | within 25 ms |
|---|---|---|---|---|---|---|
| warp | whole | 352 | 46 | +10.4 ms | 116 | 234 |
| warp | window | 351 | 35 | +9.8 ms | 143 | 279 |
| Ableton transients | whole | 352 | 60 | +11.1 ms | 104 | 218 |
| Ableton, where it agrees with the warp within 10 ms | whole | 286 | 36 | +10.5 ms | 92 | 191 |

The late shift of about one hop (12 ms) seen on 23 drum stems holds library-wide, and against
Ableton's transients as well as the warps, so it is not the warps' hand-placed offsets.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-28 library\`: `library.tsv` (every reading),
`allmarkers.tsv`, `transients.tsv` (Ableton's, per instrumental), `ableton_phase.tsv`,
`provenance.tsv` (398 of 429 carry a source video ID). Scripts in `BPM Corpus\scripts`.
