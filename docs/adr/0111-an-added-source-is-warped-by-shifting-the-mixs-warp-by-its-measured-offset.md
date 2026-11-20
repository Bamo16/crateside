# 0111 - An added source is warped by shifting the mix's warp by its measured offset

- Status: Accepted
- Date: 2026-11-20
- Builds on [ADR 0019](0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md)
  (an added source), [ADR 0103](0103-a-library-file-can-be-deleted-and-a-recut-keeps-the-saved-warp.md)
  (an added instrumental) and [ADR 0107](0107-a-warp-saved-in-crateside-is-the-maintainers-own.md)
  (a warp saved in crateside writes the whole group).
- Approved in idea by the maintainer on 2026-11-20. The measurements are in *Evidence*;
  `scripts/stem_offset.py` makes them.

## Context

A few songs have official stems released beside the mix. Live's warp needs every file of a song
sample-aligned, and an official instrumental or acapella is not: it starts at another point and
has another length. Until now the maintainer lined them up by hand, or had padded copies made
(*SUPADOPA (Official Instrumental)* in the library is one: its length equals the mix's to the
sample).

The idea: crateside finds each added source's offset to the mix; when the mix's warp is saved
(ADR 0107), it writes that warp into the added source's own `.asd`, shifted by the offset. No
padded copy is made and the official audio is never touched.

## Evidence

Offsets by cross-correlation (a whitened one, 100 Hz to 10 kHz, over both whole files), then each
10 s window of the added source searched within 1 s of that offset (the whole mix only when that
misses, because a looped beat repeats exactly and a whole-mix search alone lands on another
repeat of it). Offset d means `added[n]` matches `mix[n + d]`. All files are 44.1 kHz.

| pair | offset | windows agreeing (within 3 samples) | best band r |
|---|---|---|---|
| Gucci Gucci acapella `glF85prh3e4` against the mix's Vocal - Full stem | -2812 samples (-63.8 ms) | 18 of 19 | 0.77 |
| the same acapella against the full mix `luP5bpqhrcE` | -2812 | 18 of 19 | 0.52 |
| Huey - Pop, Lock & Drop It, official instrumental against Instrumental - Full | +19470 (441.5 ms) | 24 of 26 | 0.71 |
| the same instrumental against the full mix | +19470 | 24 of 26 | 0.68 |
| Huey official acapella against Vocal - Full | +359546 (8152.97 ms) | 24 of 25 | 0.56 |
| the same acapella against the full mix | +359546 | 24 of 25 | 0.36 |
| SUPADOPA official instrumental against the full mix | 0 | 15 of 15 | 0.77 |
| the same instrumental against the mix minus its Vocal - Full stem | 0 | 15 of 15 | 0.95 |
| control: the SUPADOPA mix against the Gucci acapella (unrelated) | none | 1 of 19 | 0.003 |

- **Huey stands in for *Poppin'*.** *Three 6 Mafia - Poppin' My Collar*'s added instrumental and
  acapella had not reached the app when this was measured (`addedSources` was empty, nothing new
  in staging or the working set), so they are **not measured**. Huey - *Pop, Lock & Drop It* has
  the same shape (an official instrumental and acapella added to a mix) and landed unpadded in
  Sample Staging the same day, so it was measured instead.
- **Why *Poppin'*'s mix is 6 s longer is not established.** The mix is 176.90 s, its music runs to
  about 173 s and fades out over the last 3 s, with no trailing silence, so the difference is not
  padding at the end. Only the instrumental itself can say where the 6 s are, and it was not
  there. The next measurement of it will say; this ADR's rules do not depend on the answer.
- **SUPADOPA's official instrumental is already aligned**, offset 0, because it is the padded
  copy; its unpadded original is not on this machine, so its raw offset was not measured. It
  shows what a finished alignment looks like: 15 of 15 windows, no spread.
- **The offset is the same against a stem and against the full mix**, to the sample, in all four
  pairs that were measured both ways. Stems are cut sample-aligned with their source. The stem
  gives the cleaner match (Gucci r 0.77 against 0.52, Huey acapella 0.56 against 0.36), because it
  holds only the part the added file also holds.
- **Steady in every pair.** The 1 to 2 windows per pair that did not agree were not a different
  offset: they were windows with no match at all, such as Gucci's acapella at 150 to 160 s
  (nothing matches there in either the stem or the mix, and the offset is back at -2813, one sample from before; why was not examined),
  and a window right at Huey's end. The offset shifts by at most 1 to 2 samples over a whole song
  (-2812 to -2814 across 190 s), which is the measurement's own jitter. **No real drift and no
  edit turned up among the real pairs.**
- **Drift and edit are shown to be detectable with files made for it**: SUPADOPA's mix with 3 s
  cut out at 60 s read as *edit* (offset 0 up to 50 s, then 132300 samples); the same file
  stretched by 20 ppm read as *drift* of -19.6 ppm; the same file with 0.5 s of silence put in
  front read as one offset of -22050 samples. A drift of 300 ppm (a different speed, 2 s over a
  song) is lost to *no match*, which is also refused.
- **Plain correlation is not the confidence.** Huey's instrumental correlates at r -0.03 over the
  whole file with an offset that 24 of 26 windows agree on to the sample: its low end (100 to
  1000 Hz) correlates at -0.40 and its top (4 to 12 kHz) at +0.71, so a different master, with a
  different low-frequency phase, of the same recording. **The best band's r is what tells a
  different master (still alignable) from a different recording (not).** The unrelated control
  reads 0.003 in every band and agrees in 1 window of 19. The weakest real pair is Huey's
  acapella against the full mix at 0.36.
- **The instrumental and the acapella of one song agree** in the only way two added sources can:
  each is steady against the same mix, so both lie on its timeline (Huey's at +441.5 ms and
  +8152.97 ms; the acapella is trimmed to start at the first vocal). They are not compared with
  each other.

## Decision

### When, and against what

1. **The offset is measured once an added source's file is in staging or the library and the mix
   has a stem** (any of its stems; their timing is the mix's). It is measured **against the stem of
   the matching kind**: an instrumental source against *Instrumental - Full*, an acapella or vocal
   source against *Vocal - Full*; with no such stem, against the track's separation source on disk
   (the working set's file or the kept sample, from `source-index.json`). The offset is the same
   either way, and the stem gives the better confidence.
2. It is **measured again only when one of the two files changes** (size or modified time), never
   on a timer or by a re-judge. It costs no GPU and runs unattended, as detection does.
3. A record per added source, in `source-offsets.json` beside the other per-song stores: the two
   files, the offset in samples, the windows (offset and r each), the best band's r, the verdict
   and when it was measured.

### What counts as confident

4. **crateside accepts an offset only when both hold**: at least **75%** of the added source's
   non-silent 10 s windows agree with the whole-file offset to within **3 samples**, and the best
   of three bands (100 to 1000 Hz, 1 to 4 kHz, 4 to 12 kHz) has **r of 0.30 or more**. Below
   either, crateside **refuses**: nothing is written and the source reads *not the same recording*
   or *not steady*. The margin is wide: the control is at 0.003 and 1 window of 19; the weakest
   real pair is at 0.36 and 24 of 25.
5. A window with no match (agreeing with nothing) does not count against the offset while the
   75% holds. It is listed by its time ("no match at 150 s") so it can be heard.

### The four outcomes

6. **A single offset** (75% or more of windows agree): accepted when 4 holds. The offset is the
   median of the agreeing windows, in whole samples. Shown as *offset +441.5 ms*, with *24 of 26
   windows*, and the unmatched windows' times.
7. **A drift** (the windows' offsets lie on a straight line, 75% of them within 3 samples of it,
   and the line moves 10 samples or more across the file): **refused.** A single shift would put
   the source on the beat at one end of the song and off it at the other, and Live's warp has no
   way to carry a different speed. Shown as *drifts 19.6 ppm (about 0.9 samples a second): another
   sample rate or speed; not written*.
8. **An edit** (two or more runs of at least two windows each, covering 75% of them, each with
   its own offset): **refused.** One shift fits only one run. Shown with each run: *offset 0 up to
   50 s, then +3.00 s from 60 s: this file is cut differently from the mix; not written*. A later
   ADR may write one warp per run if one turns up; none of the measured pairs has an edit.
9. **No match** (neither of the above and the best offset holds in less than 75% of windows, or 4
   fails on the band r): **refused**, shown as *not the same recording as the mix: a different
   source, or a different song*. This is how a stem from a different source than the mix, or than
   its sibling, is told apart from one with a different master: a different master still agrees
   window after window on one offset and has a best band r above 0.30; a different recording does
   neither.
10. A refused source keeps whatever warp it has. It is listed, with its reason, in the save's
    confirmation and on Track details, so the maintainer lines it up by hand as before.

### Writing the shifted warp

11. **When the mix's warp is saved** (ADR 0107's save), every added source of that track with an
    accepted offset gets its own `.asd` written in the same save: each sidecar stamped when the
    draft began and checked again before writing, each backed up, written with the saved-clip flag
    set, and restored by Undo. If any changed since the draft began, nothing is written (that
    ADR's rule, unchanged).
12. **The shift**: the added source's time at a marker is the mix's time minus `d / sampleRate`.
    The tempo and the beats do not change, so the marker's beat number, the bars and the tempo
    are the mix's. The added source's waveform, transients and clip gain stay its own, as Live or
    crateside's own analysis wrote them; only the warp (markers, clip start, clip end) is
    derived.
13. **The added source starts before the mix** (d below 0, as with Gucci's acapella, 64 ms
    earlier): its audio before the mix's start lies before the clip start's bar and is simply
    outside the clip. Nothing is written before it, and the clip start is the mix's.
14. **The added source starts after the mix** (d above 0, as with Huey's acapella, 8.15 s later):
    a marker that would land before the added source's first sample is dropped, and a marker is
    put at its first sample by following the mix's warp to that time, so the beat grid is the
    same on the bars after it. If the clip start's bar would land before the first sample, the clip
    start is moved to the first bar line at or after it, and the page says so.
15. **The added source ends before the mix** (the clip would run past the file): the clip ends at
    the end of the added file, at the last tempo, and nothing is padded. **It ends after the mix**
    (Gucci's acapella is 2.87 s longer): the clip ends at the end of the added file, the last
    tempo carried on past the mix's last marker. Loop stays off.
16. **An added source whose own warp the maintainer saved is not written** (ADR 0107: a stem
    warped on its own is never written); it is listed as *has its own warp*. An added source with
    no sidecar, a starter or a Warp-off clip, or a warp crateside derived before, is written, and
    a later save derives it again from the new mix warp.
17. Never an audio file: no padded copy is made, and nothing in the added source's `.flac` is
    touched.

### Where it shows

18. **On Track details, on the added source's line among the track's samples**, one line: *Offset
    to the mix: +441.5 ms (19 470 samples), steady, 24 of 26 windows*, or the refusal's wording
    from 7 to 9, with a folded list of the windows (time, offset, r) beneath it. While it is
    waiting or measuring the line says so. A *Not the same recording* source is also marked on the
    Tracks row's source count.
19. **In the warp view**, the Save confirmation lists each added source it will write, with its
    offset, and each it will not and why (refused, or has its own warp). After a save, the added
    source's warp card reads *Shifted from the mix's warp by +441.5 ms*, and says when a clip
    start moved (14).

## Consequences

- A refusal is the common safe answer, and it is the one the maintainer has always had: line
  it up by hand. Drift and edit have no writer yet because none of the real pairs needed one.
- The measurement is Python today (`scripts/stem_offset.py`). The record in 3 is what the C#
  worker will write once it is built; a worker's results are checked against that script on the
  four measured pairs.
- *Poppin' My Collar*'s two sources are measured the first time they reach the app, and any
  result that differs from this ADR's expectations (steady, one offset) is a finding to add here.
