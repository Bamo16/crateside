# beatdetect on drum stems, against the warps

*Note, 2026-09-27, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68) and Fil. Measured
on this machine with the **pre-rework** `beatdetect` (ffmpeg `8.1.2-NoMercy-MediaServer`: one BPM,
no confidence). Fil's rework of 2026-09-06 is not in this build.*

## What was run

23 steady-tempo warped library songs, picked across beatdetect's bands on the full mix (9
triplet/dotted, 4 half or double, 4 wrong, 2 close, 4 exact). The library keeps no full mixes, so
each drum stem was cut from the song's **Instrumental** stem (`stemforge-cli separate
--extract-drums`, model `htdemucs_ft`), a copy in scratch. beatdetect ran as keytag runs it: a
90 s window from 30 s in, and the whole file. *Full mix* is the ledger's figure.

**Drums-only separation took 85–211 s, median 107 s** (mean 122 s) per song.

## Results

The octave is the user's call (ADR 0038, amended): *½ or ×2* is ambiguous, not wrong.

| song | warped | full mix | drums, window | drums, whole | instrumental, window |
|---|---|---|---|---|---|
| Rob Zombie - Superbeast | 154.00 | 102.32 triplet/dotted | 153.78 exact | 102.32 triplet/dotted | 102.32 triplet/dotted |
| NXONE'S FAVORITE, Nakedleisure - NOT A DREAM | 167.00 | 110.31 triplet/dotted | 83.31 ½ or ×2 | 110.31 triplet/dotted | 110.31 triplet/dotted |
| Porter Robinson - Language | 128.00 | 84.68 triplet/dotted | 96.79 triplet/dotted | 169.31 triplet/dotted | 84.68 triplet/dotted |
| Will Atkinson, GURLI - Calling For You | 142.00 | 93.77 triplet/dotted | 93.77 triplet/dotted | 90.83 wrong | 94.44 triplet/dotted |
| Creepy Nuts - Bling-Bang-Bang-Born | 157.00 | 103.66 triplet/dotted | 156.19 close | 156.19 close | 156.19 close |
| WALK THE MOON - Anna Sun | 140.00 | 92.70 triplet/dotted | 92.70 triplet/dotted | 99.40 wrong | 92.70 triplet/dotted |
| aespa - Spicy | 115.00 | 152.98 triplet/dotted | 114.59 exact | 114.59 exact | 114.59 exact |
| T.I. - What You Know | 147.00 | 97.24 triplet/dotted | 109.40 triplet/dotted | 109.92 triplet/dotted | 109.40 triplet/dotted |
| Lil Wayne, Static Major - Lollipop | 148.00 | 110.53 triplet/dotted | 110.53 triplet/dotted | 111.13 triplet/dotted | 110.53 triplet/dotted |
| Andromedik, Lauren L'aimant - Air | 174.00 | 87.22 ½ or ×2 | 87.22 ½ or ×2 | 87.22 ½ or ×2 | 86.17 ½ or ×2 |
| Sub Focus, bbyclose - On & On | 174.00 | 86.52 ½ or ×2 | 86.52 ½ or ×2 | 99.40 wrong | 86.52 ½ or ×2 |
| Taylor Swift - Shake It Off | 160.00 | 80.00 ½ or ×2 | 80.00 ½ or ×2 | 80.00 ½ or ×2 | 80.00 ½ or ×2 |
| Yaeji - For Granted | 185.01 | 92.03 ½ or ×2 | 92.03 ½ or ×2 | 120.10 wrong | 92.03 ½ or ×2 |
| Groove Armada - At the River | 136.00 | 86.82 wrong | 89.97 triplet/dotted | 135.44 close | 86.82 wrong |
| Sarah Cothran - As the World Caves In | 124.00 | 99.40 wrong | 99.40 wrong | 99.40 wrong | 124.11 exact |
| Awich - Queendom | 105.00 | 91.90 wrong | 91.90 wrong | 137.84 wrong | 99.40 wrong |
| Delta Heavy - White Flag | 87.00 | 92.38 wrong | 99.40 wrong | 86.52 exact | 115.29 triplet/dotted |
| Ludacris - How Low | 144.00 | 143.36 close | 143.36 close | 143.36 close | 143.36 close |
| Iggy Azalea, Charli XCX - Fancy | 95.00 | 94.44 close | 94.44 close | 94.44 close | 94.44 close |
| DMX - Where The Hood At | 93.00 | 92.83 exact | 92.83 exact | 92.83 exact | 92.83 exact |
| Usher, Lil Jon, Ludacris - Yeah! | 105.00 | 104.86 exact | 104.86 exact | 104.86 exact | 104.86 exact |
| Belinda Carlisle - Heaven Is A Place On Earth | 122.89 | 122.51 exact | 122.51 exact | 122.51 exact | 122.51 exact |
| Dr. Dre, Eminem - Forgot About Dre | 133.88 | 133.73 exact | 133.73 exact | 117.83 wrong | 133.73 exact |

| reading | exact | close | ½ or ×2 | triplet/dotted | wrong |
|---|---|---|---|---|---|
| full mix | 4 | 2 | 4 | 9 | 4 |
| drums, window | 6 | 3 | 5 | 6 | 3 |
| drums, whole | 5 | 4 | 2 | 5 | 7 |
| instrumental, window | 6 | 3 | 4 | 8 | 2 |

## Observations

- **A drum stem fixed 3 of the 9 triplet/dotted misses** in the window (Rob Zombie – *Superbeast*,
  Creepy Nuts – *Bling-Bang-Bang-Born*, aespa – *Spicy*). The instrumental alone fixed 2 of those, so
  removing vocals does some of the work. Six stayed at ×2/3 or ×3/4.
- **Window against whole file disagrees often**, in both directions: Rob Zombie – *Superbeast* is exact
  only in the window, Groove Armada – *At the River* and Delta Heavy – *White Flag* only on the whole
  file.
- **Leading and trailing silence matters sometimes.** Trimming it (`silenceremove`, −50 dB) turned
  Rob Zombie – *Superbeast*'s whole-file 102.32 into 153.78 (warped 154) and left three others
  unchanged.
- **The build reports from a coarse grid**: 414 ledger readings hold 162 distinct values, about
  0.8 BPM apart near 128.
- **Its full-mix readings never exceed 163.11** in the ledger, and bottom out at 80.00 (ten times).
  Drum and bass at 170–179 (53 library songs) can only come back folded. A drum stem once read
  169.31, so it is a strong bias, not a hard cap.

## DrumSep: kick, snare and hi-hat

Each drum stem split with `MDX23C-DrumSep-aufr33-jarredou` (StemForge's new `--model` and `--keep`),
**median 80 s** a song (66–133). Scored the same way:

| reading | exact | close | ½ or ×2 | triplet/dotted | wrong |
|---|---|---|---|---|---|
| drums, window | 6 | 3 | 5 | 6 | 3 |
| kick, window | 7 | 1 | 4 | 7 | 4 |
| kick, whole | 5 | 4 | 2 | 6 | 6 |
| snare, window | 6 | 3 | 4 | 4 | 6 |
| snare, whole | 6 | 4 | 3 | 3 | 7 |
| hi-hat, window | 4 | 2 | 3 | 9 | 5 |
| hi-hat, whole | 3 | 5 | 2 | 6 | 7 |

- **No single part beats the whole drum stem** on this build. 12 of 23 songs have *some*
  drum-derived reading exact or close; none has all of them.
- **The hi-hat alone misreads at a triplet or dotted multiple most (9), the snare least (3–4).**
  Consistent with subdivisions pulling the pre-rework filter to ×2/3 and ×3/4, not proof of it.

## Drums from the full mix against drums from the instrumental

Two warped songs whose full mix was still in a crateside working set, both ×2/3 misses on the full
mix. Drums cut from each source, scored the same way:

| song | warped | drums from full mix (window / whole) | drums from instrumental (window / whole) |
|---|---|---|---|
| Mitsukiyo - Unwelcome School | 180 | 89.62 / 89.62 | 89.62 / 89.62 |
| 1991 - If Only | 174 | 115.79 / 130.45 | 115.79 / 130.45 |

Identical to the hundredth. Two songs is a hint, not a finding, but the drum model appears to pull
the same drums from either source, so the source is unlikely to change beatdetect's answer.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-27 drum stems\<song>\`: the drum stem and its
kick, snare and hi-hat, with `results.tsv` and `drumsep.tsv` beside them (their `drums_file` paths
point at the session scratch they were made in). The full-mix comparison is beside it in `2026-09-27 full mix vs instrumental`. The scripts are in
`BPM Corpus\scripts`.

## The reworked filter (added the same evening)

Built from `FiLL/ffmpeg-beatdetect` at `3f1b218` (2026-09-06 rework) with his `dev.sh` in Docker,
`TARGET=windows`, into `V:/Repos/ffmpeg-beatdetect/out/ffmpeg.exe`. Default options
(`tempo_prior=none`). Every stem above plus each song's library instrumental re-scored the same way;
`rescore-3f1b218.tsv` beside the stems has every reading, old and new.

| input | build | exact | close | ½ or ×2 | triplet/dotted | wrong |
|---|---|---|---|---|---|---|
| instrumental, whole | old | 8 | 3 | 3 | 7 | 2 |
| instrumental, whole | new | 14 | 0 | 6 | 2 | 1 |
| drums, window | old | 6 | 3 | 5 | 6 | 3 |
| drums, window | new | 14 | 0 | 4 | 2 | 3 |
| drums, whole | old | 5 | 4 | 2 | 5 | 7 |
| drums, whole | new | 16 | 0 | 4 | 1 | 2 |
| kick, whole | new | 5 | 0 | 9 | 1 | 8 |
| snare, whole | new | 0 | 0 | 22 | 0 | 1 |
| hi-hat, whole | new | 12 | 0 | 7 | 1 | 3 |

- **The triplet and dotted misses are mostly gone**: 7 → 2 on the instrumental, 5 → 1 on the drums.
- **The whole drum stem is the best input**: 16 of 23 exact, 20 of 23 up to the octave.
- **No reading is merely close any more**: the new build reports the tempo precisely (154.00 where
  the old one said 153.78), so the ~0.8 BPM grid is gone.
- **Parts alone are worse than the whole kit.** The snare reads half-time on 22 of 23, as a backbeat
  on 2 and 4 would; the kick is wrong on 8.
- **Confidence is low throughout but not useless**: median 0.11 on triplet/dotted or wrong readings,
  about 0.24 on exact and octave ones. It flags real misses; it cannot pick the octave.

## Not done

- Full mixes with the new build: the library keeps none, so the instrumental stands in for them.
- Checking the new build's `beat_offset_ms` against the warps' first marker.
