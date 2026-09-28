# 0038 — a warp's BPM is the stable tempo that holds longest

- Status: Accepted
- Date: 2026-09-26
- Step 1 of [#68](https://github.com/Bamo16/crateside/issues/68). Builds on
  [ADR 0020](0020-the-sidecar-is-parseable-and-a-warp-marker-is-a-second-and-a-beat.md) and
  [ADR 0037](0037-a-saved-default-clip-is-a-finished-warp.md).

## Context

A finished warp (ADR 0037) holds the tempo the user set by ear against the grid. The user trusts it
wherever there are drums. It beat both detectors on 332 songs (#68), so crateside can treat it as
the track's true BPM. A sidecar records markers, not a tempo, and a warp with several markers can
hold several tempos. Something has to say which one is *the* BPM.

What the user needs from a BPM, before or after warping, is the tempo of the **stable part** and
whether the tempo **changes** anywhere (owned). The shape of a change is not useful to them: they
warp a change by hand, marker by marker on the drum stem's transients. The curve would matter only
if crateside ever wrote warps itself.

Measured across the 844 saved library sidecars on 2026-09-26:

- 712 (84%) hold one marker, so one tempo.
- 37 multi-marker warps hold one tempo on at least 80% of their bars. 95 drift (live drummers, e.g.
  *Cypress Hill – Hits from the Bong*, 90.8–93.6 bar to bar) or change (*ReeK – Deity Mode*: 180, a
  drop to 132.007 for 24 bars, a ramp back to 180, a slowdown at the end).
- Tempo read per bar reproduces Ableton's tempo lane for *Deity Mode* bar for bar, and averages out
  the per-hit jitter a hand-warped ramp shows there.
- Ableton keeps more than two decimals: 305 of the 712 single-marker tempos sit exactly on two
  decimals (e.g. *Throw Some D's* 80.3904). 42 songs sit more than 0.2 from a whole number, mostly
  80s and 90s hip hop and pop. *Ready Steady Go*'s 10 markers fit one line at 128.02 within 2 ms.

## Decision

- **A stable run** is a stretch of at least 8 bars that one tempo fits, judged by how far the
  markers stray from a straight line in milliseconds rather than by bar-to-bar BPM. Silent bars, read
  from the sidecar's waveform overview, do not count towards a run. The tolerance is set against
  the library when built.
- **The BPM** is the tempo of the stable runs that together cover the most bars.
- **Every warp is one of three kinds:**
  - **steady**: one run covers the clip; every single-marker warp is steady.
  - **steady with changes**: a main tempo, and anything else (ramps, an unsteady intro, other
    stable sections, which are kept and can be listed).
  - **unsteady**: no run is long enough. The BPM is the median bar tempo, and its range is kept.
- **Stored exactly, shown whole.** Rows show the BPM as a whole number; decimals are not wanted in
  the GUI (owned).
- **A song's BPM** comes from its instrumental when its stems disagree (2 songs of 402), since a warp
  is most trustworthy where there are drums.

## Consequences

- A warped track has a BPM crateside can stand behind, and keytag's recommended BPM becomes a
  pre-warp estimate.
- The curve of a tempo change is not kept. Writing warps would need it, and would be its own
  decision.
- An unsteady track's single number is a median, not a tempo anyone played. Its range is shown with
  it.
- A tempo .01–.02 off a whole number in a modern recording is, in the user's view, the whole
  number. Rounding for display covers it, and crateside does not act on it.

## Built, 2026-09-26

The figures the decision left to the build, measured on the library that day:

- **Tolerance: 3 ms.** At 2 ms, *Ready Steady Go*'s ten markers (all within 2.0 ms of one line)
  split into two runs. At 5 ms, *Hall & Oates – Rich Girl*, a live band, gains a spurious 9-bar run.
- **A run's tempo is the median of its bars,** not the slope of the fitted line. The fit let two
  wobbly intro bars pull *T & Sugah – TumDaDaDum* to 174.03, where every other bar is 174.000.
- **Silence is a bar whose loudest sample is under −50 dBFS** in the waveform overview. Leaving it
  out changes the answer on 5 of 844 sidecars, each time from a silent stretch passing for the
  tempo (*Group X – Jonny Poopoopants* read 131.9 against a song around 170).
- The sample rate comes from the audio file, since the sidecar does not record it.

## Amended, 2026-09-27: the octave is the user's call, not ground truth

The user, on the warp's accuracy: very accurate, except that **half or double is subjective**. Which
of two tempos an octave apart is "the" BPM is a choice they would not always defend, and a drum and
bass track with a half-time section is neither one nor the other. So a warp settles the tempo up to
its octave: a detector reading ×½ or ×2 is ambiguous, not wrong. Readings at ×2/3, ×3/4 or
anything else are still errors against the warp. Counts keeps half or double as its own band for
this reason ([ADR 0041](0041-crateside-measures-bpm-detectors-against-the-warps.md)).

## Amended, 2026-09-28: the shapes are renamed

The user settled the tempo vocabulary (`CONTEXT.md`): a track written at one tempo and played
without a click is **drifting**, and an intended change is a **tempo change**. The shapes above now
read *steady*, **mostly steady** (was *steady with changes*) and **drifting** (was *unsteady*), in
code (`TempoShape`), on the Counts page and in the BPM tooltip. The rules that assign them are
unchanged. `WarpTempoReader.Version` went to 2, since the tempo cache stores the names.
