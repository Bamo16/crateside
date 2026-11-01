# Starter warps: how a steady track arrives warped

What crateside does today to give a staged stem a warp of its own making, and when it does not.
The aim: a track with one steady tempo arrives in Sample Staging already on Live's grid, its bars
on the track's downbeats. The maintainer then drags the stems in, moves the clip start to the bar
where the first verse begins, and saves the default clip.

The clip start crateside writes is only a bar line near the start of the file. The default clip
saved for the library carries the maintainer's clip start, which only they can choose; the
starter's job is to make that a matter of picking the right bar.

This page describes the current behavior and is edited in place whenever that changes. Why it
works this way is in the ADRs listed at the end.

## What the maintainer is left with

| crateside wrote | the stem's Collection | left to do by hand |
|---|---|---|
| a starter | Check beat (orange) | check the bars fall on the downbeats, move the clip start to the first verse's bar, save |
| a clip with Warp off | Warp by hand (blue) | the whole warp and the clip start |
| a lent warp (a sibling's saved warp) | Warped (green) | nothing: the sibling's clip start comes with it |

## Settings

- **`Warps:StarterWarps`**, changed from Settings while crateside runs and kept in
  `warp-settings.json`, which wins over appsettings:
  - *Off*: nothing is written, and Live analyzes the stem itself.
  - *On the grid* (the default): the marker on a beat, numbered within its bar, and the clip
    starting on the bar line.

  *Tempo only*, a marker at 0 s that only set the tempo, was removed on 2026-10-19 (ADR 0054).
- **`Warps:LendAutomatically`**: whether a sibling's saved warp is copied without asking. When a
  stem has a warp to borrow, that always comes first, so no starter is written even with lending off.

## When it runs

- **At handoff**, while the stems are still in the working set (`HandoffSidecars`). Each sidecar is
  written as its stem lands in staging, so a stem never sits there unanalyzed.
- **Over staging when Staging or the library changes, and every ten minutes besides**
  (`WarpLending`, [ADR 0060](adr/0060-the-file-workers-wake-on-a-change-and-the-timer-is-the-net.md)), for what changes later: a sibling saved, the
  setting changed, a stem put in staging by hand. Within a run, a stem is looked at again only
  when its files, its siblings' sidecars, or the tempo or downbeat it would be given changed;
  after a restart every stem is looked at again. Either way a stem that already holds the warp it
  would be lent, from the same sibling, or a starter with the same marker, is left as it is.
- **Never while Live is analyzing a stem** whose sidecar was recycled from the warp view
  (#97, showing each stem's warp in Track details).

**Order of precedence:** the maintainer's own warp, which is never touched; then a lent warp; then a
starter; then a clip with Warp off. A starter and a Warp-off clip replace each other as the tempo
allows, and a starter replaces an older starter whose marker is numbered differently. Neither
replaces a lent warp or the maintainer's own.

## The steps

1. **Pick the reference.** The stem is the upload's instrumental, else its kept source, else its
   karaoke (`WarpSiblings.TempoSources`); every stem of the upload is timed from this one stem, so
   all get the same tempo and marker, and Live can edit them together. No such stem: no starter.
   The beats are read on the upload's source on disk (the working set's download or the kept
   sample, from `source-index.json`) when there is one, else on that stem
   (`BeatReader.ReferenceFor`, [ADR 0065](adr/0065-the-starter-warps-beats-are-read-on-the-source.md)).
2. **Read the reference's tempo with beatdetect.** The pinned build (`BeatdetectFfmpeg`, today
   `1.4.0-0fc32ce`, the v1.4.0 release's code) runs `beatdetect=hpss=1` over the whole file, then over each full 30 s of it
   (`BeatReader`). Each file's reading is kept until that file changes, so a new reference (a source
   where the instrumental was) is read once, and one that falls back is read by its own. A reading from an older build than
   the pin is used until the beat worker reads the file again, which it does once every file with
   no reading has one (`BeatReader.ToRead`).
3. **Gate on the reading** (`TrackTempo.StarterBpm`). No starter when:
   - **it drifts**: the 30 s windows in their majority octave spread 0.1% or more (interquartile range
     over median); or
   - there is no reading.

   Fewer than three windows in one octave cannot show drift, so they pass this gate. The check in
   step 6 still applies. beatdetect's own `tempo_wander` and tempo segments are not used: on the
   library they refused steady tracks the check in step 6 passes, and caught nothing it misses
   (ADR 0048).
4. **Get the reference stem's transients.** Live's, when that stem has a Live analysis, else
   crateside's own onset detector (`SidecarWriter.TransientsFor`).
5. **Refine the tempo** on the louder half of those transients: the tempo within ±0.06 BPM of the
   reading, in steps of 0.0005, that best fits them to a 16th-note grid (`GridCheck.Refine`). This
   needs 32 or more transients; with fewer, the reading's tempo is used.
6. **Check that one tempo holds** (`SidecarWriter.Holds`). Lay the louder transients against the
   refined tempo in 10 s windows (`GridCheck.Against`). All three must be true:
   - at least 6 windows;
   - no step, where the offset jumps 15 ms or more and stays there;
   - every 30 s block's average offset within **10 ms** of every other's.

   A stem that fails gets a clip with Warp off.
7. **Place the marker.** Fold the transients onto one beat at the refined tempo, louder ones
   weighted to the fourth power, and take the peak within half a 16th of the reading's beat
   (`beat_offset_ms`, carried in `StarterGrid.BeatSeconds`; `GridCheck.FirstBeat`). The reading
   picks which 16th is the beat, and the transients place the marker precisely within it. A reading
   with no beat leaves the whole fold to choose. That is the first beat in the file, at most one beat
   from its start. With fewer than 32 transients no beat is placed, and the stem gets a clip with
   Warp off.
8. **Number the marker's beat in its bar** (`StarterGrid`, `SidecarWriter.StarterWarp`). Bar lines
   are every fourth beat from beat 0. When the reading's `downbeat_confidence` is 0.6 or more (1.4.0's scale; 0.03 on 1.3.x), in
   bars of four, the marker is the beat it falls on counting from beatdetect's downbeat: 0 to 3, so
   a pickup makes it 1, 2 or 3. Otherwise the marker is beat 0, a downbeat. On the library this puts
   the bar right on 66% of steady tracks, against 52% for the marker always as a downbeat (ADR 0054).
9. **Write the sidecar** for each stem (`SidecarWriter.Starter`):
   - the marker, plus Live's hidden marker 1/32 of a beat later;
   - the clip from beat 0, the bar line at or before the marker, to the end of the file, looping
     off. That bar line is usually before the audio starts;
   - the stem's own waveform and transients;
   - its clip gain to the loudness target (ADR 0059);
   - its color by kind.

   The stem goes in its Collection (table above). `written-warps.json` records, beside the sidecar,
   how the starter was made (`WrittenStarter`): the tempo written and the reading's, the marker in
   seconds and its beat, the reading's beat and downbeat with its confidence and the gate in force,
   where the beat came from (the reading's 16th placed by the fold, or the fold alone), and how the
   bar was numbered (the reading's downbeat, or beat 0 and why). The stem's warp card on the song
   page shows it as one line, its beats numbered from 1 as Live's ruler numbers them: "marker on
   beat 1: downbeat confidence 0.32 under 0.6, so beat 1 (beatdetect said beat 3)". The API returns
   it under the stem's `warp.written.starter`. Starters written before 2026-11-01 have none.

## When it does not apply

| situation | what the stem gets |
|---|---|
| `StarterWarps` is *Off* | nothing; Live analyzes it |
| a sibling holds a saved warp | the lent warp, or nothing if lending is off |
| no instrumental, kept source or karaoke stem | Warp off |
| no reading yet | Warp off, until a reading arrives |
| the windows drift | Warp off |
| one tempo does not hold within 10 ms, or has a step | Warp off |
| too few transients to place a beat (under 32) | Warp off |
| the maintainer already saved a warp | untouched |

About one steady track in eight fails the 10 ms check. On the library, 345 of 394 steady warps
pass it (MEASURED, ADR 0048).

## Known issues

- **The marker lands a 16th or half a beat off on about one steady track in thirty** (13 of 303 on
  the library, 9 of the 233 where one tempo holds; ADR 0054, 2026-10-30), where beatdetect's own
  beat is off. The 10 ms check cannot see it, since it measures phase within a 16th. The marker is
  moved by hand.

- **The bar line falls on the downbeat on about two thirds of steady tracks** (251 of the 382 read
  at the right tempo, ADR 0054). On the rest the beats are right but the bar starts on another
  beat: the downbeat is a guess under the gate (measured on 1.3.x at 0.03), and misplaced on one in five above it. The
  marker's beat is then renumbered in Live, a quick fix.
- **The downbeat comes from the source, else the instrumental.** The drum stem gave a surer one on GYARI –
  *アカリがやってきたぞっ* (*Akari ga Yattekita zo*) (0.078 against 0.002), but over 42 kept drum
  stems it was less sure and no more often right (ADR 0054).
- **A clip that starts before the audio** has not yet been opened in Live from a starter. The first
  one written is to be checked there.

## History

- [ADR 0047](adr/0047-a-starter-warp-and-where-transients-came-from.md): the starter warp
- [ADR 0048](adr/0048-stems-arrive-in-staging-already-warped.md): written at handoff, at
  crateside's own tempo; the 10 ms check; beatdetect's moving flag dropped
- [ADR 0050](adr/0050-a-warp-shows-as-itself-and-is-vouched-for-by-its-grid.md): the refined tempo
- [ADR 0054](adr/0054-a-starter-warps-marker-sits-on-a-beat-the-loud-transients-place.md): the
  marker on a beat, numbered from the downbeat; beatdetect's beat picks the 16th (2026-10-30)
- [ADR 0058](adr/0058-a-staged-stem-shows-its-state-in-live.md): Collections
- [ADR 0059](adr/0059-a-sample-arrives-at-one-loudness.md): clip gain, and Warp off when there is
  no starter
- [ADR 0065](adr/0065-the-starter-warps-beats-are-read-on-the-source.md): beatdetect reads the
  source, else the instrumental
