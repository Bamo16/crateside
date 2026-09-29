# Radiohead – Creep: a hand-warped drifting track

2026-09-29. The first track the drift flag (#75) caught that the user then warped by hand. Kept as
a drifting example for FiLL's beatdetect work (#83).

## How it was warped (the user, from memory, the same day)

"It is one of the more inconsistent ones I've come across. Only stays with the metronome for a bar
or so before I need to add another marker without fail."

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
  80.2–84.1 s), and one of 1/32 of a beat, 21 ms after the last crash (cause unknown).
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

The first four windows follow the warp's slowdown over the first two minutes closely (94.8 → 92.7
→ 91.9 → 91.4 in the warp). The last three read at double time. Which part of the track each
window covers wasn't checked, so whether the doubling lines up with the ride-cymbal section is
not known.

## Files

- `P:\RYAN\Ableton\Mashup Samples\Radiohead - Creep (Instrumental - Full).flac` and `(Vocal - Full)`,
  warped.
- `P:\RYAN\Ableton\Sample Staging\Radiohead - Creep (Drums).flac`, the AI drum stem, warped with
  the same markers, left in staging by the user for this purpose. crateside never deletes a warp
  guide itself; the row keeps asking for it to be deleted while it is there.
- An HPSS split of Creep is in `%USERPROFILE%\Music\Stems\HPSS Guide Test\` (#83).
