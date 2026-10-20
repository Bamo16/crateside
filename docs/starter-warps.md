# Starter warps: how a steady track arrives warped

What crateside does today to give a staged stem a warp of its own making, and when it does not.
The aim: a track with one steady tempo arrives in Sample Staging already on Live's grid. The
maintainer then drags the stems in, sets the clip start, and saves the default clip.

This page describes the current behaviour and is edited in place whenever that changes. Why it
works this way is in the ADRs listed at the end.

## What the maintainer is left with

| crateside wrote | the stem's Collection | left to do by hand |
|---|---|---|
| a starter, *On the grid* | Check beat (orange) | check the marker's beat, set the clip start, save |
| a starter, *Tempo only* | Place beat (yellow) | move the marker onto a beat, set the clip start, save |
| a clip with Warp off | Warp by hand (blue) | the whole warp |
| a lent warp (a sibling's saved warp) | Warped (green) | nothing |

**Known gap: which beat of the bar the marker is.** The clip starts on the marker's beat, so a clip
dropped on a bar line in the arrangement treats that beat as the downbeat. That is right only when
the first beat the transients place is a downbeat. The fix, numbering the marker's beat from
beatdetect's downbeat, is [#134](https://github.com/Bamo16/crateside/issues/134) (ADR 0054,
amended 2026-10-19).

## Settings

- **`Warps:StarterWarps`**, changed from Settings while crateside runs and kept in
  `warp-settings.json`, which wins over appsettings:
  - *Off*: nothing is written, and Live analyses the stem itself.
  - *Tempo only* (the appsettings default): one marker at the start of the audio, at crateside's tempo.
  - *On the grid*: the marker on a beat, and the clip starting there.
- **`Warps:LendAutomatically`**: whether a sibling's saved warp is copied without asking. When a
  stem has a warp to borrow, that always comes first, so no starter is written even with lending off.

## When it runs

- **At handoff**, while the stems are still in the working set (`HandoffSidecars`). Each sidecar is
  written as its stem lands in staging, so a stem never sits there unanalysed.
- **Once a minute over staging** (`WarpLending`), for what changes later: a sibling saved, the
  setting changed, a stem put in staging by hand. A stem is looked at again only when its files,
  its siblings' sidecars or its tempo changed.
- **Never while Live is analysing a stem** whose sidecar was recycled from the warp view (#97,
  the warp view).

**Order of precedence:** the maintainer's own warp, which is never touched; then a lent warp; then a
starter; then a clip with Warp off. A starter and a Warp-off clip replace each other as the tempo
allows. Neither replaces a lent warp or the maintainer's own.

## The steps

1. **Pick the reference stem.** The upload's instrumental, else its kept source, else its karaoke
   (`WarpSiblings.TempoSources`). Every stem of the upload is timed from this one stem, so all get
   the same tempo and marker, and Live can edit them together. No such stem: no starter.
2. **Read its tempo with beatdetect.** The pinned build (`BeatdetectFfmpeg`, today
   `1.3.0-1e57fec`) runs `beatdetect=hpss=1` over the whole file, then over each full 30 s of it
   (`BeatReader`). The reading is kept until the file changes, even after the pin moves.
3. **Gate on the reading** (`TrackTempo.StarterBpm`). No starter when:
   - **it moves**: more than one tempo segment, or `tempo_wander` 0.7 or more;
   - **it drifts**: the 30 s windows in their majority octave spread 0.1% or more (interquartile range
     over median); or
   - there is no reading.

   Fewer than three windows in one octave cannot show drift, so they pass this gate. The check in
   step 6 still applies.
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
7. **Place the marker** (*On the grid* only). Fold the transients onto one beat at the refined
   tempo, louder ones weighted to the fourth power, and take the peak (`GridCheck.FirstBeat`). That
   is the first beat in the file, at most one beat from its start. With fewer than 32 transients, or
   in *Tempo only*, the marker sits at 0 s.
8. **Write the sidecar** for each stem (`SidecarWriter.Starter`):
   - one marker (as Live's beat 1, or beat 0 at 0 s), plus Live's hidden marker 1/32 of a beat later;
   - the clip from the marker's beat to the end of the file, looping off;
   - the stem's own waveform and transients;
   - its clip gain to the loudness target (ADR 0059);
   - its colour by kind.

   The stem goes in its Collection (table above).

## When it does not apply

| situation | what the stem gets |
|---|---|
| `StarterWarps` is *Off* | nothing; Live analyses it |
| a sibling holds a saved warp | the lent warp, or nothing if lending is off |
| no instrumental, kept source or karaoke | Warp off |
| no reading yet | Warp off, until a reading arrives |
| beatdetect reports it as moving | Warp off |
| the windows drift | Warp off |
| one tempo does not hold within 10 ms, or has a step | Warp off |
| the maintainer already saved a warp | untouched |

About one steady track in eight fails the 10 ms check. On the library, 345 of 394 steady warps
pass it (MEASURED, ADR 0048).

## Known issues

- **The downbeat** is not used: see the gap above and #134.
- **A dead-steady track can read as moving on the pinned build.** MEASURED 2026-10-19: on GYARI –
  *アカリがやってきたぞっ* (*Akari ga Yattekita zo*), which holds within 4 ms, beatdetect 1.3.0
  gives the instrumental a `tempo_wander` of 1.831 against 0.055 on its drums. The starter it has
  was written from a reading by an older build that had no wander figure. Read again on the pinned
  build, it would get Warp off.

## History

[ADR 0047](adr/0047-a-starter-warp-and-where-transients-came-from.md) (the starter warp),
[ADR 0048](adr/0048-stems-arrive-in-staging-already-warped.md) (written at handoff, crateside's
own tempo, the 10 ms check),
[ADR 0050](adr/0050-a-warp-shows-as-itself-and-is-vouched-for-by-its-grid.md) (the refined tempo),
[ADR 0054](adr/0054-a-starter-warps-marker-sits-on-a-beat-the-loud-transients-place.md) (the
marker on a beat; the downbeat, amended),
[ADR 0058](adr/0058-a-staged-stem-shows-its-state-in-live.md) (Collections),
[ADR 0059](adr/0059-a-sample-arrives-at-one-loudness.md) (clip gain, and Warp off when there is
no starter).
