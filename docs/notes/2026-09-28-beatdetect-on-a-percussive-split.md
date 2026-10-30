# beatdetect on a percussive split (HPSS) instead of a drum stem

*Note, 2026-09-28, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68) and Fil. Measured
on this machine with Fil's reworked `beatdetect` (`3f1b218`), CPU only, on the same 23 songs as
[the drum-stem note](2026-09-27-beatdetect-on-drum-stems.md) (picked across the old build's error
types, so harder than average).*

**The question** (the user's): Fil's `stemsplit` runs Spleeter's 2-stem model inside ffmpeg. Would a
cheap, leaky drum isolation be enough to help `beatdetect`, given that analysis tolerates bleed that a
mashup stem could not?

**What was run.** librosa's median-filter harmonic-percussive separation on each library
instrumental (mono, the file's own rate, STFT 2048/512), keeping the percussive part: `margin=1`
(librosa's default) and `margin=3` (only what is clearly percussive). No model. By ear (the user's)
the result is rough, with a lot of bleed.

| input, whole file | right, same octave | right, other octave | miss | grid on the beat | within 10 ms |
|---|---|---|---|---|---|
| instrumental | 14 | 6 | 3 | 19 | 6 |
| `htdemucs_ft` drum stem | 16 | 4 | 3 | 17 | 4 |
| kick + 2 × snare + ½ hi-hat | 13 | 9 | 1 | 20 | 7 |
| HPSS, margin 1 | 19 | 3 | 1 | 21 | 5 |
| HPSS, margin 3 | 19 | 3 | 1 | 21 | 8 |

- **Paired, HPSS fixed 2 and broke none** against the instrumental it came from (Awich – *Queendom*,
  Ludacris – *How Low*), and 2 and none against the `htdemucs_ft` drum stem (NXONE'S FAVOURITE,
  Nakedleisure – *NOT A DREAM*, Sarah Cothran – *As the World Caves In*). The one tempo miss left is
  Lil Wayne, Static Major – *Lollipop* (×2/3); the one off-beat lock is aespa – *Spicy*.
- **The one-hop late shift remains** (median +8 to +11 ms on the whole file).
- **Confidence is higher on HPSS** (median 0.28 at margin 3 against 0.22 on the drum stem), and on
  *Spicy* it is higher while locked to the off-beat. Confidence does not compare inputs.
- librosa took a median 32 s per song per setting. That is Python median filtering, not what a C
  implementation would cost. Neither ffmpeg build has an HPSS filter.

23 songs is a small sample; the direction, not the size, is the finding: a model-free percussive
split did at least as well as a neural drum stem here.

The percussive files are in `%USERPROFILE%\Music\Stems\BPM Corpus\2026-09-28 hpss\`; the readings in
`hpss.tsv` beside them; the script in `BPM Corpus\scripts\hpss.py`.
