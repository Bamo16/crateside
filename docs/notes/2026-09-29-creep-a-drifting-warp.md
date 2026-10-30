# Radiohead – Creep: a hand-warped drifting track

2026-09-29. The first track the drift flag (#75) caught, then warped by hand. Kept as
a drifting example for FiLL's beatdetect work (#83).

## How it was warped (by hand, the same day)

One of the more inconsistent tracks: it stays with the metronome for a bar or so before another
marker is needed without fail.

- A marker on the first beat of the song.
- Then one on **every snare, beats 2 and 4**.
- A section near the end where the drums are **only a ride cymbal**: the downbeat and beat 3.
- At the very end a more traditional beat again, the snare replaced by a **cross-stick**: beats
  2 and 4.
- The last two bars: **two crash downbeats**, no other drums.

## The warp, as read from the `.asd` (measured)

Read with `AbletonWarpReader` from `Radiohead - Creep (Instrumental - Full).flac.asd` in Mashup
Samples. The vocal and the drum stem carry the same markers.

- Complex mode, 4/4, **172 markers**, beat 0 at 1.103 s to beat 348.03 at 226.691 s: **92.57 BPM
  overall**.
- 162 of the 171 segments are two beats long. The rest: 1 beat (3), 3 (1), 4 (3), 6 (1, at
  80.2–84.1 s), and the 1/32-beat handle Ableton adds after the last marker (`CONTEXT.md`), which puts the
  tempo past the final crash at 90.26 BPM. Across the library, 922 of 932 warped `.asd` files end in
  that handle; the other 10 (five songs, vocal and instrumental) end in an irregular gap.
- Segment tempo: min 89.10, p10 90.53, **median 92.24**, p90 95.27, max 100.46 BPM.
- By 30-second window of the warp:

  | window | warp BPM |
  |---|---|
  | 0–30 s | 94.76 |
  | 30–60 s | 92.69 |
  | 60–90 s | 91.87 |
  | 90–120 s | 91.42 |
  | 120–150 s | 91.48 |
  | 150–180 s | 93.57 |
  | 180–210 s | 92.86 |
  | 210–240 s | 91.40 |

## beatdetect's reading (from `beats.json`)

The instrumental, read with the `9.0-NoMercy-MediaServer` ffmpeg on 2026-09-29: **91.78 BPM,
confidence 0.124**, windows 93.99 · 92.96 · 91.79 · 91.47 · 180.99 · 186.77 · 191.46.

The windows are cut from 0 s in 30 s steps (`BeatReader`), and the warp's beat 0 is at 1.1 s, so
they line up with the warp's windows above. Taking the other octave as the same tempo:

| window | warp | beatdetect | as the warp's octave | off by |
|---|---|---|---|---|
| 0–30 s | 94.76 | 93.99 | 93.99 | −0.8 % |
| 30–60 s | 92.69 | 92.96 | 92.96 | +0.3 % |
| 60–90 s | 91.87 | 91.79 | 91.79 | −0.1 % |
| 90–120 s | 91.42 | 91.47 | 91.47 | +0.1 % |
| 120–150 s | 91.48 | 180.99 | 90.50 | −1.1 % |
| 150–180 s | 93.57 | 186.77 | 93.39 | −0.2 % |
| 180–210 s | 92.86 | 191.46 | 95.73 | +3.1 % |

Every window follows the warp's wandering at one octave or the other. The one real miss is
180–210 s, where the marker pattern changes (1- and 4-beat segments at 181.8–187.7 s), which is
probably the ride-cymbal section (hypothesis: the section's times were not taken down).

## Files

- `P:\RYAN\Ableton\Mashup Samples\Radiohead - Creep (Instrumental - Full).flac` and `(Vocal - Full)`,
  warped.
- `P:\RYAN\Ableton\Sample Staging\Radiohead - Creep (Drums).flac`, the AI drum stem, warped with
  the same markers, left in staging by the user for this purpose. crateside never deletes a warp
  guide itself; the row keeps asking for it to be deleted while it is there.
- An HPSS split of Creep is in `%USERPROFILE%\Music\Stems\HPSS Guide Test\` (#83).
