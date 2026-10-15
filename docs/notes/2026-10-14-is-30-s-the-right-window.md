# Is 30 s the right window? Five sizes over the 429 instrumentals

*Note, 2026-10-14, frozen. For [#106](https://github.com/Bamo16/crateside/issues/106) (closed) and
the filter issue [#21](https://github.com/Bamo16/beatdetect-harness/issues/21); decides nothing
itself. Every claim is marked **measured** (a number from a run), **owned** (read from a file or
the source) or **guessed**. Follows
[the 2026-10-13 note](2026-10-13-windows-against-wander-and-segments.md).*

## Where 30 s came from (owned)

No rationale is recorded. It first appears in the
[2026-09-28 windowed runs](2026-09-28-beatdetect-mixes-pooled-parts-and-unsteady-tempo.md) and was
never tuned. crateside reads fixed, non-overlapping windows (`BeatReader`,
`BeatReading.WindowSeconds`). The filter's own `tempo_spread` also uses 30 s windows, with a 15 s
hop (FiLL's comment 6180687502 on harness #21, and `window_tempi` in `src/af_beatdetect.c`, which
steps by half a window).

## What was run

- **Tracks:** the 429 instrumentals of `2026-09-28 steadiness\shapes.tsv`; 427 distinct song
  names (two share a name and collapse, as in the 2026-10-13 note).
- **Windows:** for each size N in 15, 20, 30, 45 and 60 s, consecutive non-overlapping windows
  from the start of the file (`ffmpeg -ss S -t N`), the whole file covered, a short tail dropped.
- **A window's tempo:** `60000 / beat_interval_ms` from the last frame of one filter run **on that
  cut segment**. This is not the filter's per-window comb peak inside a whole-file run, which is
  what `tempo_spread` uses, so these windows are crateside's way, not FiLL's.
- **Build:** the pinned `1.3.0-1e57fec`. Six workers.
- **Scoring:** #75's rule (`scripts\rescore_windows.py`), applied by `score_sweep.py`: fold each
  reading to the median octave; keep those within 6 % of the folded median in the majority octave;
  **a track needs three kept readings or it is not scored**; the flag is the IQR of the kept
  readings as a % of their median. A window with no reading is dropped before that.
- **What the script counted, which changes the reading:** the scored set differs by size. A
  longer window leaves fewer windows per track (median per track: 14, 11, 7, 4, 3), so fewer
  tracks reach three. A track not scored is not caught and not falsely flagged; it simply is
  absent from both columns.
- **A doubled write:** two copies of the sweep ran for ten minutes and both appended to the files.
  Lines are deduped by song, the later one winning; the two copies' readings are the same runs.

## Result (measured)

Caught of unsteady, falsely flagged of steady, and of steady-with-changes, **on each size's own
scored set**:

| size | scored (steady / changes / unsteady) | IQR ≥ 0.05 | IQR ≥ 0.1 | IQR ≥ 0.15 |
|---|---|---|---|---|
| 15 s | 364 / 23 / 35 | 32 · 39 · 11 | 31 · 12 · 8 | 29 · 5 · 5 |
| 20 s | 362 / 23 / 35 | 33 · 22 · 9 | 28 · 10 · 7 | 26 · 5 · 5 |
| 30 s | 356 / 23 / 35 | 29 · 14 · 5 | 26 · 5 · 3 | 25 · 2 · 2 |
| 45 s | 331 / 22 / 29 | 22 · 7 · 4 | 21 · 1 · 3 | 20 · 1 · 3 |
| 60 s | 264 / 19 / 20 | 16 · 2 · 3 | 15 · 1 · 2 | 15 · 1 · 2 |

Each cell is unsteady caught · steady falsely flagged · with changes flagged. Thresholds 0.03,
0.07, 0.2 and 0.3, and the common set scorable at every size (295 tracks), are in
`2026-10-14 windows\scored.txt`.

## What it shows

1. **The right threshold moves with the window size** (measured). Five steady tracks are falsely
   flagged at IQR 0.15 for 15 s windows, 0.15 for 20 s, 0.1 for 30 s, and about 0.05 for 45 s
   (two or fewer from 0.07). A figure from one size cannot be read against another's threshold.
2. **At the same number of false alarms, smaller windows catch more unsteady tracks** (measured;
   direction only). At five falsely flagged steady tracks: 15 s catches 29 of 35, 20 s and 30 s
   catch 26 of 35. Larger windows reach two or fewer false alarms but on a scored set that is
   smaller: 45 s catches 22 of the 29 it can score at 2, and 60 s 16 of the 20 it can score at 2.
   The differences between sizes are two or three tracks of 35; no significance is claimed.
3. **Large windows cannot score short unsteady tracks** (measured). The sweep holds 36 unsteady
   song names. 15, 20 and 30 s score 35 of them; 45 s scores 29 and 60 s scores 20. The ones left
   out are short: median duration 90 s for the 7 left out at 45 s and 120 s for the 16 left out at
   60 s, against 210 to 240 s for those scored. A fixed long window silently drops the tracks it
   would most need to flag.
4. **False alarms on steady tracks fall on short tracks at 30 s, not at 15 s** (measured; very
   few tracks). At 30 s with IQR ≥ 0.07: 5 of 62 steady tracks under 180 s are flagged, against 3
   of 171 at 180 to 240 s and 1 of 123 above 240 s. At 15 s with IQR ≥ 0.1 the rates are flat: 1 of
   63, 6 of 177, 5 of 124. By warp tempo (under 100, 100 to 130, above 130 BPM) neither size shows
   a pattern (2, 3, 4 of 74, 130, 152 at 30 s; 5, 3, 4 of 75, 132, 157 at 15 s).

## What it does not show

- **A better size is not established.** 15 s looks better than 30 s at matched false alarms,
  the margin is three tracks, and it was scored with a threshold chosen by looking at the same
  data (guessed to generalise, not tested outside it).
- **A dynamic size is not tested** (guessed): finding 4 suggests a short track wants more, smaller
  windows, but no run here varied the size by track.
- **Overlapping windows were not swept**, which is what the filter does (a 15 s hop). Windows that
  are cut and run separately may also read differently from the filter's windows within one run.
- **The 30 s row here is not #75's** (measured): at IQR ≥ 0.1 this sweep reads 26 · 5 · 3 on 414
  scored tracks, where #75 read 28 · 11 · 7 on 416. The sizes in this note are comparable with each
  other (one script, one build, one set of cuts) and not with #75's numbers. Cause unknown; a guess
  is #75's earlier build and its different window starts.

## The filter's own `tempo_spread`, scored the same day (measured and owned)

Same 416 scored tracks, build `1.3.0-1e57fec`, from `grid.jsonl` and `readings.jsonl`, and again
independently on the harness's library sweep (`1.3.0-87d24fc`, 418 tracks; its table is
`results\2026-10-14-1.3.0-87d24fc-library\tempo-spread-vs-shape.tsv` in `beatdetect-harness`). The
two agree to within a track.

| flag | unsteady | steady | with changes |
|---|---|---|---|
| #75 windows, spread ≥ 0.1 % | 28 | 11 | 7 |
| `tempo_spread` ≥ 0.05 | 28 | 13 | 6 |
| `tempo_spread` ≥ 0.1 | 25 | 8 | 5 |
| spread absent | 5 | **152** | 6 |
| FiLL's full gate at 0.1 (absent, ≥ 0.1, or segments ≠ 1) | 31 | **169** | 15 |

The gate sends 169 of the steady tracks to a human because `tempo_spread` is absent on 152 of
them, 98 of which the filter read at the warp's own tempo (about 100 by crateside's count). **Cause (owned, from the source):**
`compute_tempo_spread` drops every window whose raw comb peak is more than 4 % from the whole-file
tempo and returns nothing when fewer than three remain, so a window one octave off is lost.
FiLL's agent reports that folding each window to the nearest octave first brings the absent steady
tracks from 137 of 350 to 4; that figure is its own simulation, not re-run here.

## Where the files are

`%USERPROFILE%\Music\Stems\BPM Corpus\2026-10-14 windows\`: `sweep-15|20|30|45|60.jsonl` (one
line per song, the window tempi), `sweep.log`, `grid.jsonl` and `readings.jsonl` (the
`tempo_spread` inputs), `scored.txt` (the full score). Scripts in
`%USERPROFILE%\Music\Stems\BPM Corpus\scripts\`: `sweep.py`, `score_sweep.py`, `by_length.py`,
`spread_score.py`.
