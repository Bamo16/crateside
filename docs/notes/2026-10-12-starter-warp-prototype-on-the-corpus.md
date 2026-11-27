# The starter warp (#101) prototyped on the corpus

Build `1.3.0-1e57fec` (FiLL's `feat/tempo-track`), 29 corpus tracks, each file's audio as the harness holds it,
against `corpus/truth.jsonl`. **Measured** unless marked. The marker here is the filter's first beat (or first
downbeat when sure); the comparison is that beat against the nearest beat of Bamo's warp, positive late. Phase and
tempo are separate: the drift column is a linear fit of the same errors, per minute.

## Findings

- **Plain `beatdetect` places the marker; `hpss=1` does not.** With `hpss=1` the first beat lands half a beat off on
  HUMBLE. (+398 ms at 75 BPM) and XG LEFT RIGHT (+196 ms at 150), both steady. Plain is within 13 ms of the warp on all
  21 steady tracks. `hpss=1` reads HUMBLE at the right octave (75, plain says 150), which is what it buys; it did not
  help any steady track's phase. Crateside's `BeatReader` already runs plain.
- **A marker on the first beat is good to its drift.** The worst steady drift is 2.2 ms/min (Call Me Maybe, As It Was; Sugar 1.6,
  Humble 1.4), so over a 4.75 min instrumental the far end of the grid is up to ~10 ms off the
  warp's, from a marker at the start. Not measured: whether centring the marker (#72's transients) shrinks that.
- **Beat 1 gated at 0.03:** 12 tracks with a known clip-start bar clear it, 8 are right. The four wrong:
  Sunflower (read at 180, warped at 90, so a bar of 4 is two), XG (triplet-related), Yes (live, moves), Get Low (moves,
  read an octave off). Below the gate, 9 of 17 land on a bar by chance.
- **Moves.** More than one tempo segment or `tempo_wander` â‰¥ 0.7 catches 7 of the 8 tracks whose warp moves and
  false-alarms on 2 steady ones: Hello (read an octave off, 5 segments) and Children (3 segments, from its beatless
  intro; arguably the "needs eyes" signal rather than a false one). It misses Yes.
- **The old window flag (#75, spread â‰¥ 0.1 % over 30 s windows) catches 4 of the same 8 with no false alarm.** It
  misses bad guy (135 â†’ 120 step), Lose Yourself, Get Low and Yes. #106's replacement is better on recall, worse on
  precision by the two above.

## Per track


| track | moves (warp) | BPM read / warp | first beat vs warp, ms (plain / hpss) | drift ms/min | downbeat conf | beat 1 on a bar |
|---|---|---|---|---|---|---|
| adele-hello | no | 158.00 / 79.00 (half_or_double) | 2.9 / 7.7 | -1.06 | 0.005 | yes |
| adele-rolling-in-the-deep | no | 105.00 / 105.00 (exact) | -2.2 / -3.2 | 0.48 | 0.001 | yes |
| alan-walker-faded | no | 180.00 / 90.00 (exact) | 8.0 / 7.2 | 0.39 | 0.015 | yes |
| anita-ward-ring-my-bell | yes | 125.63 / – (-) | 201.5 / 98.6 | -52.03 | 0.000 | no |
| billie-eilish-bad-guy | yes | 135.00 / – (-) | -10.6 / -13.7 | -1.34 | 0.000 | no |
| bon-jovi-livin-on-a-prayer | yes | 122.80 / – (-) | -2.0 / -43.3 | -3.12 | 0.013 | no |
| bow-wow-wow-i-want-candy | yes | 109.22 / – (-) | -39.7 / 183.9 | 56.23 | 0.002 | no |
| carly-rae-jepsen-call-me-maybe | no | 120.00 / 120.00 (exact) | -12.3 / -12.3 | 2.20 | 0.005 | no |
| chainsmokers-closer | no | 190.00 / 95.00 (half_or_double) | 8.9 / 0.4 | 0.44 | 0.011 | no |
| daft-punk-around-the-world | no | 121.29 / 121.29 (exact) | 0.4 / 0.5 | -0.59 | 0.000 | no |
| daft-punk-get-lucky | no | 116.04 / 116.05 (exact) | -8.7 / -8.5 | 0.31 | 0.000 | no |
| eminem-lose-yourself | yes | 171.44 / – (-) | -12.1 / -4.5 | -0.39 | 0.004 | yes |
| harry-styles-as-it-was | no | 173.99 / 174.00 (exact) | -5.6 / -8.1 | 2.16 | 0.093 | yes |
| kendrick-lamar-humble | no | 150.00 / 75.00 (exact) | 3.2 / 397.6 | -1.41 | 0.126 | yes |
| kid-laroi-bieber-stay | no | 170.00 / 170.00 (exact) | 1.3 / -2.7 | 0.28 | 0.072 | yes |
| killers-mr-brightside | no | 147.96 / 147.96 (exact) | -7.4 / -10.6 | 1.05 | 0.006 | yes |
| lil-jon-get-low | yes | 50.50 / – (-) | -295.4 / 3.9 | 106.87 | 0.098 | no |
| mark-ronson-uptown-funk | no | 115.00 / 115.00 (exact) | -0.1 / 0.6 | 0.85 | 0.001 | yes |
| maroon-5-sugar | no | 120.00 / 120.00 (exact) | -10.6 / -10.3 | 1.62 | 0.369 | yes |
| pharrell-happy | no | 160.00 / 160.00 (exact) | 7.0 / 7.5 | 0.59 | 0.585 | yes |
| pink-floyd-money | yes | 125.66 / – (-) | -10.7 / -178.8 | 4.17 | 0.001 | yes |
| post-malone-sunflower | no | 180.00 / 90.00 (half_or_double) | -0.9 / -0.7 | 0.80 | 0.246 | no |
| robert-miles-children | no | 137.08 / 137.08 (exact) | -5.4 / -5.1 | 0.40 | 0.002 | yes |
| snoop-dogg-gin-and-juice | no | 94.62 / 94.62 (exact) | -10.2 / -10.2 | -1.02 | 0.808 | yes |
| stardust-music-sounds-better-with-you | no | 124.21 / 124.21 (exact) | 0.1 / 10.9 | -0.37 | 0.007 | yes |
| taylor-swift-shake-it-off | no | 160.00 / 160.00 (exact) | 4.2 / 7.1 | 0.03 | 0.855 | yes |
| weeknd-blinding-lights | no | 171.00 / 171.00 (exact) | 9.8 / 6.5 | -0.77 | 0.033 | yes |
| xg-left-right | no | 150.00 / 150.00 (exact) | -2.1 / 196.1 | -0.33 | 0.326 | no |
| yes-owner-of-a-lonely-heart | yes | 125.00 / – (-) | -1.0 / -1.0 | 1.51 | 0.216 | no |
