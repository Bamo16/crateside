# How spread is the library's loudness?

*Note, 2026-10-19, frozen. Step 1 of [#131](https://github.com/Bamo16/crateside/issues/131)
(give every sample a clip gain that brings it to one loudness target). Decides nothing. Every
figure is **MEASURED** unless tagged otherwise.*

## How it was measured

ffmpeg 9.0-NoMercy-MediaServer's `ebur128=framelog=verbose` over every audio file in
`Mashup Samples` (the library, 1,034 files) and `Sample Staging` (78), whole file, with the
run's frame log parsed for the short-term (3 s) series. Per file: integrated LUFS, and the 50th,
90th, 95th and 99th percentile of short-term loudness over frames above -70 LUFS. 1,112 of 1,113
files give a reading; `Johnny Pearson - Action in Memphis (Vocal - Full)` is silence (-70.0).
Kind comes from the trailing parenthesis of the file name; a name with none is a **source**
(the original track).

## Integrated loudness (LUFS)

| group | n | p5 | p25 | median | p75 | p95 | sd | p5–p95 |
|---|---|---|---|---|---|---|---|---|
| all | 1112 | -19.3 | -15.6 | -13.8 | -12.4 | -9.8 | 3.1 | 9.5 |
| library | 1034 | -18.8 | -15.5 | -13.7 | -12.3 | -9.7 | 2.9 | 9.1 |
| staging | 78 | -23.8 | -17.4 | -15.1 | -13.8 | -10.8 | 4.6 | 13.0 |
| Instrumental - Full | 510 | -16.5 | -14.3 | -12.9 | -11.7 | -10.1 | 2.0 | 6.4 |
| Vocal - Full | 513 | -20.6 | -16.8 | -14.8 | -13.6 | -12.4 | 2.9 | 8.2 |
| Drums | 26 | -23.8 | -19.3 | -17.4 | -15.9 | -13.8 | 3.4 | 10.0 |
| source | 42 | -11.7 | -9.8 | -8.4 | -7.0 | -5.1 | 2.6 | 6.6 |

(Five Instrumental - Clean files sit at a median of -12.7.)

## What it says about the issue's question

- **The spread is real.** Only 49% of samples are within ±1.5 dB of the library median
  (-13.8 LUFS), 75% within ±3 dB. The issue's stop condition ("most samples already within
  ~3 dB") is borderline on the bulk, but the tails are long: p5 to p95 is 9.5 dB, and the
  quietest vocals sit at -28 to -37 LUFS.
- **Vocals are quieter than instrumentals by a median of 2.0 dB** of integrated loudness, over
  505 songs that have both (p10 -0.4, p90 +5.6). Short-term p95 gives 1.9 dB. A single target
  with no per-kind offset therefore lifts every vocal about 2 dB relative to its own
  instrumental, which is the "stems stop summing to the source's balance" the issue accepted.
- **Integrated and the loud-parts measure differ by a nearly constant amount.** Short-term p95
  minus integrated has a median of 1.8 dB for vocals (p10 1.2, p90 2.8) and 1.7 dB for
  instrumentals (p10 0.9, p90 2.8). The ranking of files barely changes between the two; the
  per-file wobble of about ±1 dB is the only place they would disagree. Whether the ear prefers
  one is step 2 and needs listening.
- **Short-term p50 is the wrong metric for vocals.** Its spread is the widest (sd 4.4 for
  vocals against 2.9 integrated), because gaps in a vocal pull the median down: the effect the
  issue feared of integrated loudness is stronger in p50 than in integrated.
- **Sources are 4–6 dB louder than the stems** (median -8.4), mastered hot; stems keep the
  separator's headroom. A target must be chosen for the stems; the 42 sources in staging or the
  library sit well above any plausible target and would be turned down about 10 dB.
- **Staging has the widest tail** (sd 4.6, p5 -23.8): a freshly separated stem, before anything
  has been judged, is quieter and more varied than what has been filed.

## What was not done

- **Step 2** (integrated against an upper percentile of short-term loudness, judged by ear on
  vocal/instrumental pairs) and **step 3** (the target) need the maintainer listening. A
  candidate to listen to: the two are close enough that the choice is between ~-14 LUFS
  integrated and ~-12 LUFS short-term p95 as the same level; -18 LUFS (the issue's guess) is
  4 dB below the library median, so it would turn most files down.
- The sidecars' present `SampleVolume` values were not read, so how many hand-set gains exist
  (to be discarded) is unknown.
- True peak was not captured (the parse did not match ffmpeg's summary line), so headroom for
  files that would be turned up is unmeasured.

## Raw data

`%LOCALAPPDATA%\crateside\loudness-2026-10-19.csv`: one row per file with integrated, LRA and the
short-term percentiles, and the file path. Outside the repo, because the paths are this machine's.
