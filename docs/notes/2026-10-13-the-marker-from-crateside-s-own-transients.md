# The marker from crateside's own transients, and the half-beat locks

*Note, 2026-10-13, frozen. For [#72](https://github.com/Bamo16/crateside/issues/72); decides nothing
itself. Follows [the earlier note](2026-10-13-placing-the-marker-from-many-transients.md), same 353
tracks, same pinned build, same truth and scoring. The transients here are `OnsetDetector`'s, run on
each instrumental through `FfmpegAudioDecoder`, **not Live's**.*

## Does the gain hold on crateside's detector

Counts within 5 / 10 / 25 ms of the warp at 5% of the track, then off the beat (a quarter beat or
more). The earlier note's Live column is repeated for comparison.

| method | Live's transients | crateside's transients |
|---|---|---|
| A, #101's placement | 116 / 208 / 298, 44 off | 114 / 204 / 294, 48 off |
| B, fold at the filter's tempo | 242 / 281 / 316, 32 off | 212 / 262 / 301, 48 off |
| C, tempo scanned | 251 / 284 / 321, 29 off | 219 / 264 / 303, 49 off |
| D, scanned, phase held near A | 245 / 282 / 314, 32 off | 228 / 278 / 317, 35 off |

(A differs slightly between columns because the track length is taken from the last transient.)

- **Yes.** Through crateside's own detector the fold still beats #101's placement: within 5 ms from
  114 to 212–228, within 10 ms from 204 to 262–278.
- **It is a little worse than on Live's transients** (B: 262 against 281 within 10 ms, 48 against 32
  off the beat), and the half-beat locks are what grow. Holding the phase near #101's grid (D) recovers
  most of it.

## Cutting the half-beat locks

Weighting each transient by its energy to a power before folding (energy is scaled so a file's
strongest is 1.0), so that loud hits (kicks, snares) outweigh quiet ones (hats). Fold at the filter's
tempo, unconstrained, 353 tracks:

| power | within 5 / 10 ms | off the beat |
|---|---|---|
| 1 (as above) | 212 / 262 | 48 |
| 2 | 222 / 269 | 45 |
| 3 | 226 / 276 | 35 |
| **4** | **236 / 282** | **26** |
| 5 | 236 / 280 | 29 |
| 6 | 239 / 281 | 32 |
| 8 | 229 / 274 | 37 |
| 12 | 220 / 267 | 39 |
| 20 | 209 / 260 | 44 |

- **Off the beat falls from 48 to 26–35 across powers 3 to 6**, the best being 4: no worse on
  precision, and below #101's 48. Held near #101's grid the same weighting gives 34–37 off the beat,
  so the unconstrained fold does better here.
- **The power was chosen on this same set** (353 tracks, picked from a sweep), so the exact figure is
  optimistic. The plateau across 3 to 6 says the effect is not one lucky value; whether it holds on
  tracks outside this set is untested.
- A threshold on energy (0.15, 0.3) changes little.
- *Guessed:* the gain is from kicks and snares being the loud transients and landing on the beat,
  while hi-hats and off-beat chords are quieter. Not checked against the drum stems.

## What follows

- **Measured:** the many-transients fold, on crateside's own detector, beats #101's placement on every
  count, with 26–35 of 353 tracks half a beat off against 48.
- **Not measured:** drum stems (68 tracks have one); tracks outside the 353; the half-beat tracks that
  remain (which they are, and whether the warp itself sits there).
- **Not addressed:** the octave and which beat is beat 1 (#103, #105).

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-10-13 marker\`: `onsets.jsonl` (crateside's transients),
`an72-onsets.json`. Scripts: `scripts\marker_score.py` (takes the transients file and an output),
`scripts\marker_weight.py`, `scripts\marker_onsets.cs.txt` (the throwaway runner, a console project
referencing `Crateside.Core`).
