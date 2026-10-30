# beatdetect's beat grid against the warps

*Note, 2026-09-28, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68) and Fil. Measured
on this machine with Fil's reworked `beatdetect` (`3f1b218`, `V:\Repos\ffmpeg-beatdetect\out\ffmpeg.exe`),
on the same 23 songs and stems as [the drum-stem note](2026-09-27-beatdetect-on-drum-stems.md).*

## What was compared

**The warp's grid.** All 23 songs are single-marker warps, each marker on a whole beat, so the warp
fixes a beat phase and a tempo (read with `AbletonWarpReader` from the instrumental's `.asd`). Every
drum stem has exactly as many samples as its instrumental, so the stems share the warp's timeline.

**beatdetect's grid.** The last frame's `beat_offset_ms` and `beat_interval_ms`: beat *n* at
`offset + n × interval`. Run on the whole file and on the 90 s window from 30 s in (offset + 30 s).
Where its tempo is the warp's, half or double, each warp beat is measured to the nearest detector
beat, folded to the finer grid. *At marker* is that error at the warp marker; *worst in song* the
largest over the file, which includes drift from any tempo error.

**Where the onsets are.** Independently of beatdetect: each part's onset envelope (rise in log energy,
2 ms hops) folded onto the warp's beat period, and the peak's position after the warp beat. This
checks the warps themselves, and shows which part sits where.

## Results, whole drum stem

| song | beat ms | onsets: drums / kick / snare / hi-hat | beatdetect, drums whole | at marker | worst in song |
|---|---|---|---|---|---|
| Rob Zombie - Superbeast | 389.6 | -3 / -1 / -7 / +7 | 154.00 | +0 | 5 |
| NXONE'S FAVORITE, Nakedleisure - NOT A DREAM | 359.3 | -0 / -74 / -0 / -2 | 41.76 (other) |  |  |
| Porter Robinson - Language | 468.7 | -2 / +1 / -2 / -2 | 128.05 | +37 | 104 |
| Will Atkinson, GURLI - Calling For You | 422.5 | +0 / +0 / +0 / +207 | 141.96 | +18 | 58 |
| Creepy Nuts - Bling-Bang-Bang-Born | 382.2 | +1 / +1 / -5 / +1 | 157.03 | +9 | 25 |
| WALK THE MOON - Anna Sun | 428.6 | -16 / -16 / -20 / +177 | 140.00 | -193 | 194 |
| aespa - Spicy | 521.7 | -1 / +259 / -1 / -1 | 114.99 | +258 | 261 |
| T.I. - What You Know | 408.2 | +13 / +13 / +13 / +13 | 73.51 (½) | +11 | 17 |
| Lil Wayne, Static Major - Lollipop | 405.4 | +1 / +201 / -0 / +201 | 98.66 (other) |  |  |
| Andromedik, Lauren L'aimant - Air | 344.8 | -2 / +0 / -2 / -2 | 174.00 | +56 | 57 |
| Sub Focus, bbyclose - On & On | 344.8 | -2 / -2 / -2 / +169 | 173.99 | +16 | 22 |
| Taylor Swift - Shake It Off | 375.0 | +183 / +179 / -18 / +185 | 160.03 | +4 | 43 |
| Yaeji - For Granted | 324.3 | -5 / -5 / -5 / -5 | 92.53 (½) | +37 | 40 |
| Groove Armada - At the River | 441.2 | +1 / +1 / +1 / -0 | 136.03 | +7 | 64 |
| Sarah Cothran - As the World Caves In | 483.9 | -13 / +125 / -7 / -143 | 206.23 (other) |  |  |
| Awich - Queendom | 571.4 | -284 / -282 / +1 / -144 | 104.98 | +30 | 42 |
| Delta Heavy - White Flag | 689.7 | -3 / -3 / -3 / -3 | 86.99 | -12 | 12 |
| Ludacris - How Low | 416.7 | -2 / -2 / -2 / -194 | 143.98 | +12 | 32 |
| Iggy Azalea, Charli XCX - Fancy | 631.6 | +1 / +1 / +1 / +1 | 47.50 (½) | +285 | 288 |
| DMX - Where The Hood At | 645.2 | -14 / -14 / -14 / -6 | 93.02 | -18 | 74 |
| Usher, Lil Jon, Ludacris - Yeah! | 571.4 | -4 / -8 / -4 / -280 | 105.00 | +12 | 26 |
| Belinda Carlisle - Heaven Is A Place On Earth | 488.3 | +5 / -241 / +5 / +5 | 122.85 | +24 | 86 |
| Dr. Dre, Eminem - Forgot About Dre | 448.2 | -221 / -221 / +5 / -221 | 66.93 (½) | +49 | 73 |

Signed: positive is later than the warp beat. *(½)* reads half the warp's tempo; *other* is not the
tempo, half or double, and has no grid comparison. Snare readings are half-time on 22 of 23 and not
shown.

| input | span | tempo, ½ or ×2 | on the beat | median error at marker | median absolute | within 10 ms |
|---|---|---|---|---|---|---|
| drums | whole | 20 | 17 | +12.4 | 16.4 | 4 |
| drums | window | 18 | 18 | +3.5 | 7.3 | 11 |
| instrumental | whole | 20 | 19 | +11.9 | 15.0 | 6 |
| instrumental | window | 21 | 20 | +11.9 | 12.8 | 5 |

*On the beat*: within a quarter beat of the warp's grid at the marker; the error columns cover only
those. Every row is in `offsets.tsv`, kick, snare and hi-hat included.

## Observations

- **The warps are on the onsets.** The summed drum stem's onsets peak within 5 ms of the warp beat on
  16 of 23 songs and within 16 ms on 20. On T.I. – *What You Know* (+13) and DMX – *Where The Hood At* (−14)
  every part agrees, so the marker sits a little off the transients rather than the audio being odd.
- **The snare marks the beat most reliably.** Its onsets are within 20 ms of the warp beat on all 23,
  within 7 ms on 19, and never on the half-beat. The summed kit peaks near the half-beat on 3 songs
  (Taylor Swift – *Shake It Off*, Awich – *Queendom*, Dr. Dre, Eminem – *Forgot About Dre*), the kick alone on 6, the hi-hat on 8.
- **beatdetect finds the beat on 17 of the 20 drum stems whose tempo it gets.** The three it misses by
  about half a beat: aespa – *Spicy*, where it follows the kick (kick +259 ms, grid +258); WALK THE
  MOON – *Anna Sun*; and Iggy Azalea, Charli XCX – *Fancy*, read at half tempo. **All three are on the
  beat in the window** (drums: +1, −4 and −27 ms).
  By the user's ear, *Spicy*'s beat is unconventional, *Fancy*'s sparse, and *Anna Sun* has long
  stretches with no drums or only an off-beat snare: consistent with those sections pulling a
  whole-file fit onto the off-beat, not checked section by section.
- **Its beats land late, by about one hop.** Median +12.4 ms on the drum stem and +11.9 on the
  instrumental, against a default hop of 12 ms. Only 4 of 17 are within 10 ms. That the bias is the
  same on both inputs and about one hop suggests the onset-latency correction in `frame_to_ms`
  (1.5 hops) is short by a hop; not tested.
- **The window does better than the whole file** at the marker (median absolute 7.3 ms against 16.4
  on the drums), plausibly because a grid fitted to 90 s drifts less than one fitted to the song.
- **Tempo precision limits a whole-song grid.** On same-tempo readings the BPM is off by a median
  0.02 and at most 0.05 (drums, whole), which drifts up to 104 ms across a song (Porter Robinson – *Language*,
  128.05 against 128.00). A one-marker warp needs the tempo to about 0.01 or better
  (Paul Oakenfold, Asher D – *Ready Steady Go*: 128.02 against 128.00 was the difference). `beat_interval_ms` carries more
  precision than the two-decimal `bpm`.

## For placing a marker (#72)

Not yet good enough as it stands: a warp marker wants a few milliseconds (ADR 0038 uses 3 ms), and the
grid is typically 10–20 ms late with the tempo drifting on top. The one-hop bias, if it is one, is
the cheapest thing to fix. Separately, and untested: folding the drum stem's or the snare's onsets on
a known tempo found the beat within 5 ms on most songs here, but that used the warp's own tempo, so
it says nothing yet about doing it from beatdetect's.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-28 beat grid\`: `markers.tsv` (the warp markers),
`offsets.tsv` (every reading against the warp), `onsets.tsv` (each part's onset phase). The scripts
are in `BPM Corpus\scripts`: `markers.cs`, `offsets.py`, `onsets.py`, `summary.py`.
