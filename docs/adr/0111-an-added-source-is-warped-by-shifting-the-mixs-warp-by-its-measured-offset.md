# 0111 - An added source is warped by shifting the mix's warp by its measured offset

- Status: Accepted
- Date: 2026-11-20
- Builds on [ADR 0019](0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md)
  (an added source), [ADR 0103](0103-a-library-file-can-be-deleted-and-a-recut-keeps-the-saved-warp.md)
  (an added instrumental) and [ADR 0107](0107-a-warp-saved-in-crateside-is-the-maintainers-own.md)
  (a warp saved in crateside writes the whole group).
- Approved by the maintainer on 2026-11-20. The measurements are in *Evidence*;
  `scripts/stem_offset.py` makes them.

## Context

A few songs have official stems released beside the mix. Live's warp needs every file of a song
sample-aligned, and an official instrumental or acapella is not: it starts at another point, has
another length, and may hold only part of the song. Until now the maintainer lined them up by hand,
or had corrected copies made. *SUPADOPA (Official Instrumental)* in the library is one: as
released it starts 6 ms before the mix and has a different length, and the library's copy was
edited to start and end where the mix does, to the sample.

The model is multicam audio sync, as Premiere's "synchronize by audio" does it. Several recordings
of one event, each covering part of it, are lined up by their shared audio. Here the **mix is the
reference**, and an added source is lined up to it by the audio they share. The question is *does
it align?*, never *is it the same version?*: a stretch of one file that the other lacks is
**unmatched content**, reported and never counted as disagreement.

The mix is the reference for two reasons. It is the only file that holds everything the others
hold in part: an instrumental and an acapella share almost no audio with each other, but each
shares its half with the mix. And the warp is saved on the mix, so every shift is relative to it
anyway.

crateside finds each added source's offset to the mix; when the mix's warp is saved (ADR 0107), it
writes that warp into the added source's own `.asd`, shifted by the offset. No padded copy is made
and the official audio is never touched.

## Evidence

Method: the added source is cut into 5 s windows. Each non-silent window is cross-correlated against
the whole mix with a whitened (PHAT, 100 Hz to 10 kHz) correlation, and its **confidence is the
peak's height over that window's own correlation noise** (peak-to-sidelobe ratio, PSR: the peak minus
the median of every lag, over 1.48 times the median absolute deviation). The window looks within 1 s
of the whole-file offset first, because a looped beat repeats exactly; a peak found elsewhere in the
mix counts only if it persists (see 8). Offset d means `added[n]` matches `mix[n + d]`: d above 0 says
the mix has d more samples before the same sound. All files are 44.1 kHz; the mix is always the
full-mix download (source index), the added source the file as staged or in the library.

| case | verdict | offset | windows confident | weakest confident PSR | unmatched in the added file | mix not covered |
|---|---|---|---|---|---|---|
| Huey - *Pop, Lock & Drop It* (`zOmccz4mCBQ`), official instrumental | steady | +19470 (+441.5 ms) | 51 of 52 | 15.0 | 0-5 s | 0-5.4 s, 257.8-263.3 s |
| the same, official acappella (trimmed to the first vocal) | steady | +359546 (+8152.97 ms) | 45 of 50, 42 on the offset | 12.2 | 20-25 s, 95-105 s, 175-185 s | 0-8.2 s and four 5-10 s gaps, 243.2-263.3 s |
| Kreayshawn - *Gucci Gucci* (`luP5bpqhrcE`), acapella `glF85prh3e4` | steady | -2812 (-63.8 ms) | 35 of 39 | 20.4 | 75-85 s, 150-160 s | 74.9-84.9 s, 149.9-159.9 s |
| Lil Wayne - *A Milli* (`xX-N3B8ulnI`), the "A Cappella" the maintainer downloaded (`C_DIhh3sWO0`) | steady | +251414 (+5701.0 ms) | 41 of 43 | 25.9 | 5-10 s, 210-215 s | 0-5.7 s, 10.7-15.7 s, 215.7-223.0 s |
| Ayatsuno Yuni - *SUPADOPA* (`1JZk8V95PI8`), the raw official instrumental (`gSnbWAQ4J0E`) | steady | -273 (-6.19 ms) | 31 of 31 | 23.8 | none | none |
| the same, the library's corrected copy of that instrumental | steady | 0 | 31 of 31 | 23.8 | none | none |
| Polo & Pan - *Ani Kuni*: the music video (`ootQs7sVulY`) against the art track `3rQLRRFFEWk` | **edit** | +729680 (+16.55 s) over 0-50 s; +1429449 (+32.41 s) over 65-165 s; +1429394 over 165-201.5 s | 41 of 41 | 25.1 | none | 0-16.5 s, 66.5-82.4 s, 234.0-277.5 s |
| DJ Fresh - *How Love Begins*: the official video (`1SVlqcTVQdk`) against the art track `jE46h-AenT0` | not the same recording | none | 10 of 40, no common offset, also after the speed retry | 12.1 | most of the file | not given |
| *Gucci Gucci* karaoke (`dnSNWoFEZHU`, re-recorded) against the real mix | not the same recording | none | 0 of 38 | none | 5-195 s | not given |
| unrelated: the *SUPADOPA* mix against the *Gucci Gucci* acapella | not the same recording | none | 0 of 39 | none | all | not given |
| unrelated: the *Pop, Lock & Drop It* mix against the *Ani Kuni* video | not the same recording | none | 0 of 41 | none | all | not given |

Made with files for it, from the *SUPADOPA* mix: 3 s cut out at 60 s reads *edit*; the file stretched
by 20 ppm reads *drift*, +20 ppm (+0.9 samples a second); 0.5 s of silence put in front reads steady
at -22050 samples; its first 7 s and its last 31 s cut off read steady at +308700 samples. A
stretch of 300 ppm (2 s over a song) is no longer confident in a consistent line (29 of 31 windows
confident, sharing no offset) and reads *not the same recording*, which is refused as well.

- **The threshold is 12 PSR, and its margin is narrow on one side and wide on the other.** The
  strongest window of any pair that is not the same recording is 10.4 (the unrelated pair, at its
  best peak anywhere in the mix; 9.7 at the offset). The weakest window that is a real match at the
  right offset, in the pairs above, is 12.2. At the level of a file the margin is wide: the three
  non-matching pairs have 0 confident windows of 38 to 41, and the real pairs have 90 to 100% of
  theirs. Windows between 9 and 12 exist inside real pairs (up to 11.9 at the right offset: the
  mix and the file differ there, or the content is thin), and are treated as unmatched.
- **The mix alone aligns every added source.** *A Milli*'s acapella, which lacks the repeated "A
  MILLI" sample that the separation models smeared across both stems, aligns to the mix at 41 of 43
  windows with a weakest PSR of 25.9; no stem was needed. A matching stem as a second opinion (the
  window also tried against *Vocal - Full* or *Instrumental - Full* where the mix was weak) was
  tried on four pairs: it changed no verdict, confirmed 4 windows of about 190 (PSR 15 to 26 on the stem,
  each already inside a steady verdict), and confirmed nothing in the stretches the mix lacks (its
  PSR there is 3). **The stem is not used.**
- **Huey's instrumental ends 5.4 s before the mix because it is cut early with a quick fade.**
  It ends at 257.38 s of its own timeline, which is 257.82 s of the mix. Over its last 0.6 s it fades
  from -22 dB to -77 dB and its final samples are exactly zero; the mix is at about -25 dB at that
  moment and goes on to fade out slowly over 5.4 s, down to -97 dB. The mix's last 5.4 s are
  instrumental (the *Instrumental - Full* stem carries them and *Vocal - Full* is silent there), so
  the official file is shorter by an early cut, not by trimmed silence, and the mix does not
  have padding at its end.
- **Gucci's acapella sings a different chorus, and its windows at 75-85 s and 150-160 s are
  unmatched.** In the mix, "One big room, full of bad bitches" runs through each chorus and the
  "Gucci Gucci, Louis Louis" hook joins only in its second half. The acapella drops "One big room"
  and sings the hook throughout. So in the chorus's first half the acapella has the hook where the
  mix has the other line: those windows sit at PSR 4 to 5 against the mix at their place, and
  match the mix's hook at 13 to 24 s instead (PSR 160 to 173). The vocal stem is as loud there as
  anywhere, so it cannot tell them apart; the match is the evidence. Every other window agrees to
  within 1 sample of -2812.
- ***SUPADOPA*'s raw official instrumental starts 6.19 ms before the mix** (-273 samples), as the
  maintainer remembered it: tiny. The library's corrected copy sits at exactly 0 in 31 of 31
  windows, the finished alignment.
- ***Ani Kuni* is an edit, with three sections one file lacks.** The art track has 16.5 s before
  where the video's audio begins, 15.9 s at 66.5-82.4 s that the video lacks, and 43.5 s at its end
  (the drumless outro) that the video lacks. The first and the last shift nothing: the part after
  them is on one offset. The middle one shifts everything after it, from +16.55 s to +32.41 s.
  Between 65-165 s and 165-201.5 s the offset steps by 55 samples (1.2 ms), a third run.
- ***How Love Begins*'s video is not the same recording as its art track, retry included**
  (10 of 40 windows confident, no common offset; the best window is 20.5). The offsets of the
  confident windows slide by about 45 samples a second, about 1000 ppm, the size of video's
  1000/1001 speed change, so rule 12's retry was run on it: resampled by 1000/1001, 1001/1000,
  24/25 and 25/24, it still reads 10 of 40 sharing no offset, in both the script and the C#
  worker. The two files are 197.5 s and 199.1 s long, a 0.8% difference no known speed change
  explains, so they are two different masters or mixes. It does not show the section the
  maintainer expected, so that claim is **not measured**; *Ani Kuni* is the measured edit.
- **A karaoke that is a re-recording does not align; two that are the original instrumental
  do.** The *Gucci Gucci* karaoke reads 0 of 38 windows. Two uploads sold as *Pop, Lock & Drop It*
  karaoke (`2UczgbKeo_0`, `BF6oneKfvMM`) aligned steady, to -5.13 s and -6.60 s, at 52 of 53 and 50
  of 53 windows (measured with a threshold of 15): they are the original backing track with a
  different start, so they are not examples of a different recording.
- **Offsets wander by up to 10 samples inside a real match.** *A Milli*'s acapella reads 251404 to
  251414 across its first 100 s (vocal-dominated windows) and 251414 after it. A tolerance of
  0.5 ms (22 samples) holds every real pair; the 55-sample (1.25 ms) step in *Ani Kuni* stays a
  separate run.
- **A repeated hook is not an edit.** Windows that match the mix at another place appear in four
  real pairs: *Gucci Gucci* at 75-85 s and 150-160 s (the main offset resumes on both sides), *Pop,
  Lock & Drop It*'s acapella at its last 13 s (a run at the end that matches the mix's first
  hook). They are reported as matching elsewhere and do not vote.

## Decision

### When, and against what

1. **The offset is measured once an added source's file is in staging or the library and the
   track's mix is on disk** (the working set's file or the kept sample, from `source-index.json`).
   **The reference is always the mix**: the added source needs no stem and no type (instrumental
   or acapella) to be aligned. A stem is not consulted.
2. It is **measured again only when one of the two files changes** (size or modified time), never
   on a timer or by a re-judge. It costs no GPU and runs unattended, as detection does. While the
   mix is not on disk the record stays and is not measured again.
3. A record per added source, in `source-offsets.json` beside the other per-song stores: the two
   files (path, size, modified time), the offset in samples, each window (start, offset, PSR,
   confident or not), the runs, the unmatched spans of both files, the verdict and when it was
   measured.

### What counts as confident

4. **A window is confident at 12 PSR or more**, within 1 s of the whole-file offset first and
   anywhere in the mix only when that fails. Silent windows (RMS under 1e-4) are not windows.
5. **Only confident windows vote.** A window under 12 is **unmatched content**: it is listed by
   its span ("not in the mix: 75-85 s") and never counts as disagreement. The mix's own spans that
   no confident window lands on are listed the same way ("not in this file: 257.8-263.3 s").
6. **Two offsets are the same offset within 0.5 ms.** Tolerances are in time, not samples, so
   they hold at any sample rate; offsets are still stored in samples.
7. **A source must have at least 3 confident windows and at least a quarter of its windows
   confident**, or it is *not the same recording* (the non-matching pairs have none; the real
   ones have 90% or more).
8. **A confident peak found away from the whole-file offset counts only as a run of two or more
   consecutive windows on one offset, and not when the main offset resumes right after it.** A
   hook the mix repeats is then unmatched content at this place, and a loop that repeats exactly
   cannot pose as an edit.

### The verdicts

9. **Aligned** (at least 90% of the confident windows on one offset): accepted. The offset is the
   median of those windows, in whole samples. Unmatched spans of either file, a section at the head
   or the tail that one file lacks (*Huey*'s instrumental ends 5.4 s early, *SUPADOPA*'s raw
   starts 6 ms early), and material the mix repeats do not change it.
10. **Drift** (four or more consecutive confident windows lie on a line, within 0.5 ms, that
    moves 1 ms or more): **refused.** A single shift would sit on the beat at one end of the song
    and off it at the other. A constant speed factor could be written (scale every marker's time,
    then shift), but sources that need it are rare and should be swapped for the audio release, so
    it is not built. A speed difference too large to hold windows on a line (300 ppm in the test
    above) falls to rule 12's retry.
11. **Edit** (two or more runs of at least two windows each, on distinct offsets): **refused.**
    One shift fits only one run. A section **at the head or the tail** that one file has and the
    other lacks is not an edit: it shifts nothing after it, and reads *aligned* with that span
    unmatched. A section **in the middle** that one file lacks moves everything after it, and is
    an edit (*Ani Kuni*). It is not a mismatch: the runs are the same recording.
12. **Not the same recording**: too few confident windows (7), or confident windows that share no
    single offset, run or line. **Before reading this, and before reading an edit (a
    1000/1001 slide makes confident windows on many offsets, which reads as one), the added source
    is measured again resampled by each known speed change**: 1000/1001 and 1001/1000 (video frame rates), 24/25 and
    25/24 (film shown on European TV). If one reads aligned, the verdict is *drift* with that
    factor (*Runs 0.1% fast against the mix; use the audio release*), and refused as in 10.
13. A refused source keeps whatever warp it has and is listed, with its reason, so the
    maintainer lines it up by hand as before.

### Writing the shifted warp

14. **Only an aligned source is written.** When the mix's warp is saved (ADR 0107's save), every
    added source of that track with an aligned record whose two files still have the size and
    modified time that were measured gets its own `.asd` written in the same save: each sidecar
    stamped when the draft began and checked again before writing, each backed up, written with
    the saved-clip flag set, and restored by Undo. If any changed since the draft began, nothing
    is written (that ADR's rule, unchanged). A record gone stale is measured again first and is not
    written in that save.
15. **The shift**: the added source's time at a marker is the mix's time minus `d / sampleRate`.
    The tempo and the beat numbers do not change, so the beats, the bars and the tempo are the
    mix's. The added source's waveform, transients and clip gain stay its own; only the warp
    (markers, clip start, clip end) is derived.
16. **The added source starts before the mix** (d below 0, as with *SUPADOPA*'s raw instrumental and
    Gucci's acapella): every marker's time moves later. Its audio before the mix's start lies
    before the clip start and is simply outside the clip. The clip start's beat is the mix's.
17. **The added source starts after the mix** (d above 0, as with Huey's acappella, 8.15 s later):
    a marker that would land before the added file's first sample is dropped, and one is put at
    that sample, its beat read off the mix's warp at the mix time d / sampleRate, so the grid is
    the same on every bar after it. If the clip start's bar would land before the first sample,
    the clip start moves to the first bar line at or after it, and the page says so.
18. **The added source ends before the mix** (Huey's instrumental, 5.4 s short): a marker past
    the file's end is dropped, and the clip ends at the end of the added file at the mix's last
    tempo; nothing is padded. **It ends after the mix** (Gucci's acapella, 2.87 s longer): the clip
    ends at the end of the added file, the last tempo carried on past the mix's last marker. Loop
    stays off.
19. **An added source whose own warp the maintainer saved is never written** (ADR 0107: a stem
    warped on its own is never written); it is listed as *has its own warp*. An added source with
    no sidecar, a starter or a Warp-off clip, or a warp crateside derived before, is written, and a
    later save derives it again from the new mix warp.
20. Never an audio file: no audio is padded or touched, and nothing in the added source's `.flac`
    changes.

### Where it shows

21. **On Track details, on the added source's line among the track's samples**, one line per
    verdict, with a folded list of the windows (time, offset, PSR) beneath it:
    - aligned: *Aligned to the mix: +441.5 ms (19 470 samples), 51 of 52 windows*, then the
      unmatched spans, each said as "only in this file: 0-5 s" or "only in the mix: 257.8-263.3 s";
    - edit: *Cut differently from the mix: +16.55 s up to 50 s, +32.41 s from 65 s to 165 s; not
      written. Split the clip in Live where the cut is*;
    - drift: *Runs at another speed than the mix: +20 ppm; not written*, or with a known factor
      as in 12;
    - not the same recording: *Does not line up with the mix: 0 of 38 windows; not written*.

    While it waits or measures the line says so. (The Tracks list has no per-row source count, so
    nothing is marked there.)
22. **In the warp view**, the Save confirmation lists each added source it will write, with its
    offset, and each it will not and why (edit, drift, not the same recording, has its own warp,
    measuring). After a save, the added source's warp card reads *Shifted from the mix's warp by
    +441.5 ms*, and says when a clip start moved (17).

## Consequences

- A refusal is the common safe answer, and it is the one the maintainer has always had: line it up
  by hand. Edit has no writer, since it would need a split clip. Drift has none by choice (10).
- The measurement is built in C# (`Crateside.Core.SourceOffsets`), and `scripts/stem_offset.py` stays
  as its reference: on every pair in the table the worker reads the same verdict and offset, and
  a confident-window count within one of the script's. A measurement takes 15 to 95 s on a normal
  pair; a source that reads as an edit or as not the same recording is read five times (the speed
  retry), 6 minutes for *Ani Kuni*.
- The verdict rests on what is counted, and says so: how many windows were confident and which
  spans are unmatched. The threshold of 12 has a narrow window-level margin (10.4 against 12.2) and
  rests on a handful of pairs; a pair that lands between the two is a finding to add here.
- The two *Pop, Lock & Drop It* "karaoke" uploads are the original backing track, so the case of a
  true re-recording of that song is measured by the *Gucci Gucci* karaoke alone.
