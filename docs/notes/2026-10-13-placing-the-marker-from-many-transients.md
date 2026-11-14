# Placing a stable track's marker from many transients, against beatdetect's grid

*Note, 2026-10-13, frozen. For [#72](https://github.com/Bamo16/crateside/issues/72); decides nothing
itself. Measured on this machine with the pinned `1.3.0-1e57fec`, whole files, CPU only. The
transients are Live's own, from each instrumental's `.asd` (the
[2026-09-28 note](2026-09-28-beatdetect-across-the-library.md)'s `transients.tsv`), **not
crateside's detector**: see the last section.*

## What was counted

- **Tracks:** the 363 single-marker steady warps of that note. 10 are dropped because the filter read
  another tempo class (not ×2 or ÷2 of the warp's), leaving **353** (*measured*).
- **Truth:** the user's warp grid, owned as good to 5–10 ms and occasionally 25 ms. Results under about
  10 ms say little about which method is better; the gaps below are well over that.
- **Error:** the method's grid against the warp's, at 5% of the track (where a marker would sit),
  folded to the finer of the two beat periods. "Worst" is the largest of the errors at 5, 50 and 95%,
  so it includes tempo error. "Off the beat" is an error over a quarter of a beat.
- **Tempo and octave:** the filter's tempo in the warp's octave (the octave is the user's call).
  Where a method scans tempo it searches ±0.3 BPM for the sharpest fold.
- **The 81 misses of #72 are not reproduced.** They came from scratch scripts that were not kept; the
  nearest method here, a fold at the filter's tempo, misses 72 beyond 10 ms.

## Methods

- **A, #101's placement:** the filter's `beat_offset_ms` and `bpm`, as `StarterGrid` uses them.
- **B:** fold the transients at the filter's tempo, weighted by energy; the peak is the marker.
- **C:** B with the tempo scanned.
- **D, E:** B and C kept within a quarter beat of A's phase.
- **F:** B on the first 30 s only.

## Result

353 tracks. Counts are within 5 / 10 / 25 ms, then off the beat.

| method | at 5% of track | worst of 5, 50, 95% |
|---|---|---|
| A, #101 | 116 / 208 / 298, 44 off | 73 / 163 / 277, 61 off |
| B, fold at filter's tempo | 242 / 281 / 316, 32 off | 192 / 251 / 312, 32 off |
| C, tempo scanned | 251 / 284 / 321, 29 off | 211 / 255 / 315, 29 off |
| D, scanned, phase held near A | 245 / 282 / 314, 32 off | 210 / 259 / 307, 34 off |
| E, B held near A | 240 / 282 / 313, 33 off | 192 / 263 / 309, 33 off |
| F, first 30 s only | 238 / 279 / 310, 36 off | 168 / 239 / 301, 36 off |

Median error on the beat: A 6.6 ms, B 2.9, C 2.5.

- **Transients roughly double the hits within 5 ms** (116 to 242–251) and add 73–76 within 10 ms.
  Scanning the tempo adds little over the filter's own (C against B: 3 more within 10 ms, 19 more
  within 5 ms across the track), because the filter's tempo is already near enough.
- **Using the first 30 s only is worse than the whole file** (F against B): the whole-file fold is not
  being smeared by tempo error on this set.

## What causes the misses

B's 72 misses beyond 10 ms (*measured*):

| error as a share of the beat | misses |
|---|---|
| half a beat off (over 0.42 of a beat either way) | 31 |
| within ±10% of the beat (10 ms to about 40 ms: close, not a subdivision) | 38 |
| elsewhere (a third or quarter) | 3 |

- **Wrong subdivision is 31 of 72**: the fold locks half a beat off. The fold peak is the strongest
  transient position, and on an off-beat-heavy track that is the "and".
- **The other 38 are imprecision**, not subdivision. With the warp good to 5–10 ms, a share of them
  are probably the warp's, not the method's (*guessed*; not listened to).
- **A is the arbiter of the beat only half the time.** Where A and B differ by over a quarter of a beat
  (64 tracks), B is within 10 ms on 33, A on 20, and neither on 11. Where they agree (289), B is within
  10 ms on 248 and A on 193. Holding the phase near A (D, E) gains nothing over B, since A is wrong on
  44 tracks by half a beat itself. Filter confidence and fold sharpness do not tell the two cases apart
  (medians 0.21 against 0.25, and 19 against 21).

## Does it improve on #101

Yes, on Live's transients: within 10 ms from 208 to 281–284 of 353, within 5 ms from 116 to 242–251,
and across the whole track from 163 to 251–255. It does not remove the half-beat errors (29–32
against A's 44); that needs something beyond the transients' positions, such as which are kicks and
snares, which the drum stem could give.

## What this does not show

- **It used Live's transients, which exist only after Ableton analyzes the file.** The method has to
  run on crateside's own detector (`OnsetDetector`), at the stage the starter warp is written. That
  detector's recall on instrumentals is 90.7% with a median offset of 1.2 ms
  ([transients note](2026-10-02-transients-against-lives.md)), so *guessed*: close to Live's, not
  measured through this fold. This is the step to measure before building.
- Instrumentals, not drum stems (68 tracks have both). A drum stem would give cleaner transients.
- Warps with changes and unsteady warps: not in the set.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-10-13 marker\` (`grid.jsonl`, the filter's readings;
`an72.json`, every method's errors); scripts `scripts\marker_run.py` and `scripts\marker_score.py`.
