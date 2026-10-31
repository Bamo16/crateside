# 0054 — a starter warp's marker sits on a beat the loud transients place

- Status: Accepted; amended 2026-10-19 (below): the marker's beat is numbered from the downbeat
  where the filter is sure of it, and *Tempo only* is removed
- Date: 2026-10-13
- Amends [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md), which put the starter
  warp's one marker at the start of the audio. Builds [#72](https://github.com/Bamo16/crateside/issues/72)
  for [#101](https://github.com/Bamo16/crateside/issues/101).

## Context

A starter warp set the tempo and left the phase to the user, who moved the marker onto a beat by
hand. The user's bar is any beat of the grid, not the downbeat (2026-10-13): beat 1 and the octave
stay theirs, and a marker already on a beat saves most of the move.

*Corrected 2026-10-19: this misread the request.* The maintainer wanted the marker on a clear
transient so the drum stem shows what is under it, not on a stretch of silence. Which beat of the
bar it is was never meant to be left to them. See the amendment below.

Two measurements, on the 353 steady single-marker warps of the library against the user's own
warps, with the pinned beatdetect build ([first note](../notes/2026-10-13-placing-the-marker-from-many-transients.md),
[second](../notes/2026-10-13-the-marker-from-crateside-s-own-transients.md)):

- beatdetect's own beat offset (what `StarterGrid` reads) puts the marker within 10 ms on 204 and
  half a beat off on 48.
- Folding crateside's own transients onto one beat at the starter's tempo, the louder ones weighted
  to the fourth power, puts it within 10 ms on 275 and half a beat off on 31 (the built code, run on
  the same tracks). The power was chosen on this set; 3 to 6 all do well.

## Decision

**The starter warp's marker is written on the first beat the transients place** (`GridCheck.FirstBeat`),
as beat 1, and the clip starts on that beat and runs to the end of the file. A clip start between
beats leaves the clip off the arrangement's grid, so the audio before the first beat is left out of
the clip (2026-10-13, found on Whitney Houston – *How Will I Know*). With fewer than 32 transients nothing is placed and the marker stays where
ADR 0047 put it: beat 0 at the start of the audio.

- **The whole group is timed from one stem**: the instrumental its tempo is read from. Timed each
  from its own transients, *How Will I Know*'s drums, instrumental and vocal got three tempos
  (119.5175, 119.5160, 119.5405) and three markers (0.18 s, 0.14 s, 0.43 s), and Live would not edit
  them together (*3 audio clips with different Warp Markers*). ADR 0047's rule that every stem gets
  the same marker holds.
- **Beat 1 is a beat, not a bar line.** Which beat starts a bar is unknown and stays the user's.
  *(Superseded 2026-10-19 by the amendment below.)*
- **The tempo is the one the starter already refines** (ADR 0050); the phase is taken at that tempo.
- **The octave is not touched.** The fold is at the starter's tempo whichever octave that is.
- **Opened in Live** on *How Will I Know* (2026-10-13): the marker and tempo are right, and the clip
  start, which began between beats, now starts on one.

**Amended 2026-10-19: the marker's beat is numbered from the downbeat where the filter is sure of
it** ([#134](https://github.com/Bamo16/crateside/issues/134)). Where the marker sits and which beat
it is are separate. It still sits on the first beat the transients place. Which beat of the bar that
is comes from beatdetect's downbeat, so a pickup can make it beat 2, 3 or 4. The clip starts on a bar
line, so a clip dropped on a bar in the arrangement keeps the track's bars. Where the downbeat
confidence is under the working threshold, the starter is written as above and the downbeat is
numbered as a downbeat, as before.

- **The case that showed it** (MEASURED 2026-10-19): GYARI – *アカリがやってきたぞっ* (*Akari ga
  Yattekita zo*). The marker sat at 0.4395 s as beat 1.0, the clip starting there. beatdetect 1.3.0
  puts the bar lines at ~0.00 s and every 1.76 s after it on the drums (confidence 0.078), the
  instrumental (0.002) and the source mix (0.001). In the arrangement, the track's bars landed one
  beat off.
- **Built 2026-10-19** (`StarterGrid`, `SidecarWriter.StarterWarp`). The downbeat is trusted from
  `downbeat_confidence` 0.03, in bars of four; otherwise the marker's beat is numbered as a downbeat,
  as before. MEASURED that day against the 382 steady warps of the library read in the right octave
  by beatdetect 1.3.0, taking the warp's bar lines as the true downbeats: this rule puts the bar on
  the right beat on 251 (66%), against 199 (52%) for the first beat alone and 232 (61%) for the
  downbeat at any confidence. 0.01, 0.05, 0.1 and 0.3 give 65%, 64%, 63% and 61%. About a third
  still need the marker's beat changed by hand.
- **The numbering.** Bar lines are every fourth beat from beat 0. The marker is beat 0, 1, 2 or 3,
  and the clip starts at beat 0, the bar line at or before the marker. That bar line is usually
  before the audio (the marker is the first beat in the file); the maintainer has had clips reach
  past the audio into silence in Live, and the first starter written this way is to be checked
  there.
- **The downbeat is read from the reference stem**, the instrumental, like the tempo. Reading it
  from the drum stem was proposed (on GYARI's drums the confidence was 0.078 against 0.002).
  MEASURED 2026-10-19 on the 42 kept drum stems (warp guides) whose instrumental holds a steady
  warp, both read in its octave by 1.3.0 with `hpss=1`: the drums are less sure (median confidence
  0.017 against 0.039), and no rule using them does better. Beat 1 lands right on 24 with the rule
  built, 23 with the drums' downbeat at the same gate, 22 with the drums first and the instrumental
  second. On this small set the rule built also does no better than the first beat alone (24).
- **A starter already written is written again** when its marker's beat or clip start differs from
  the one crateside would now write, so staged starters take the numbering once their instrumental
  is read on the pinned build. The beat worker reads again any instrumental whose reading is from an
  older build than the pin, after every one with no reading: 553 of the 570 kept readings were from
  1.1.0, which reports no downbeat.

**Amended 2026-10-19: *Tempo only* is removed, and a stem whose beat cannot be placed arrives with
Warp off.** The maintainer's reasons: a tempo-only starter was rarely useful, and a marker that
might or might not sit on a beat makes every marker harder to read. It could only ever help a
steady track, and a steady track is now placed on the grid. The setting is *Off* or *On the
grid*, and *On the grid* is the default. Under *On the grid*, a stem with too few transients to
place a beat (under 32) used to get a marker at 0 s. Now it gets ADR 0059's clip with Warp off,
the same as a tempo that does not hold. A `warp-settings.json` that still says *Tempo only* reads
as no choice made, so appsettings applies. The maintainer may bring the mode back.

## Consequences

- About one track in eleven still lands half a beat off (31 of 353 on the corpus). It is moved the
  same way as before.
- The filter's own beat offset is not used for placement. `StarterGrid`, which reads it, is not
  wired into the writer; it stays for the downbeat, its confidence gate and the moving flags.
- **It is a setting, not a given** (2026-10-14; *Tempo only* removed 2026-10-19, above). `Warps:StarterWarps` replaces `WriteStarterWarps` and
  has three values: *Off* (nothing is written, so Live analyzes the stem with its own transients,
  which the beatdetect harness needs), *Tempo only* (the marker and clip at the start of the audio,
  ADR 0047's starter, with the group's shared tempo) and *On the grid* (this ADR). Settings changes it
  while crateside runs and keeps the choice in `warp-settings.json`, which wins over appsettings. The
  default is *Tempo only*: the placement has been seen in Live on one track.

**Amended 2026-10-30.** beatdetect 1.4.0 rescaled `downbeat_confidence`, so the gate is 0.6 on it (dd86fea, `StarterGrid.DownbeatSureAt`); the 0.03 above and its counts are 1.3.x's.
