# 0049 — a BPM shows only the precision crateside can vouch for

- Status: Accepted; its nominal tempo superseded by [ADR 0050](0050-a-warp-shows-as-itself-and-is-vouched-for-by-its-grid.md)
- Date: 2026-10-02
- Builds on [ADR 0038](0038-a-warps-bpm-is-the-stable-tempo-that-holds-longest.md) and
  [ADR 0048](0048-stems-arrive-in-staging-already-warped.md). Wording and look go to design brief 13.

## Context

Crateside rounds every BPM it shows to a whole number. That made sense when beatdetect was too rough
to trust past that. It no longer is: on the steady tracks the user has warped, crateside's own
whole-file reading lands within 0.01 BPM of the warp on 95%, and 132 of 437 steady warps are not
whole numbers at all (Mr Oizo – *Jo* 119.50, Eminem – *My Name Is* 85.52)
([note](../notes/2026-10-02-beatdetect-precision-against-warps.md)). Ableton sets an arrangement's
tempo to two decimals.

What was asked for, 2026-10-02:

- **A BPM read at a glance has to say what it means about the track.** If `150` sometimes means
  "measured exactly" and sometimes "roughly", it can never be trusted to mean the first.
- **Once a warp exists it is the source of record**, everywhere a BPM appears, charts included.
  Any other BPM shown beside it says what it is.
- **A drifting tempo shows that it drifts**, with soft whole-number figures. A track that is
  steady apart from an intro or interlude shows its steady tempo and says that a part changes. A
  track that really changes tempo (ReeK – *Deity Mode*, Laur – *Sound Chimera*) needs only to say
  so. That is the rarest case and the bar is low.

Measured the same day, on the user's sidecars and crateside's readings:

- **An exact warp is one the user typed or set.** 146 one-marker warps sit on a whole or half number
  to a millionth of a BPM, and only 19 within 0.0005 without being exact. Snapping a transient lands
  on a sample-resolution position, so it essentially never gives an exact number. The user types the
  tempo when they have decided what it is.
- **A snapped warp is usually a hair off the true tempo.** Flo Rida – *Low* is warped at 128.002.
  Where the reading sits on the grid and the warp does not, the user expects the warp to be the one
  that is off (#91).
- **The reading says when it can be trusted to the hundredth.** When its 30 s windows agree within
  0.02%, it lands within 0.01 of the warp on 296 of 304. Its confidence figure does not predict
  that.
- **Real tempos sit off the grid too**, and must never be snapped: *Ready Steady Go* reads 128.021
  and is warped at 128.02. 50 Cent – *In Da Club* reads 90.009 and is warped at 90.008.

## Decision

### One tempo type, one formatter

Every BPM crateside shows is a **tempo**: a value, where it came from, and its shape. The tempo
decides how it is written, and only one formatter writes it. A test fails the build if a page or
component formats a BPM number itself. That is what lets a bare number be trusted: nothing can show
one without the evidence behind it.

**Where it came from**, strongest first:

1. **The user's warp**: the instrumental's where there is one (ADR 0038).
2. **Crateside's reading**: the whole-file beatdetect figure on the instrumental, with its windows'
   spread.
3. **keytag's estimate**: shown only while there is neither, and never as exact. Whether it survives
   at all is keytag#2.

The headline everywhere (Tracks, the song page, Counts' charts) is the strongest source there is.
The song page may show the others beside it, each saying what it is.

### The nominal tempo

*Superseded by ADR 0050, 2026-10-02: nothing is snapped; a warp shows as itself.*

A tempo's value is its **nominal tempo**: the figure the track was made at, when the evidence says
so, else the figure as measured.

- **An exact warp is the user's declaration.** A warp within a millionth of a whole or half number
  is that number.
- **A snapped warp, or a reading on its own, takes the nearest whole or half number** when the
  reading is within 0.003 BPM of it and its windows agree within 0.02%. On 2026-10-02 that was 181
  of 432 steady tracks; the warp agreed within 0.005 on 173 of them. *Ready Steady Go*, 0.021 off,
  stays as read.
- **Otherwise the value is the figure as measured:** the warp's slope, or the reading.

The raw figures are always kept. The nominal tempo never overwrites the warp's slope or the reading,
and a ground-truth export for FiLL carries all three.

Starter warps (ADR 0048) are written at the nominal tempo, so a whole-number track gets a marker
slope of exactly 128, not 128.002.

### How it is written

| the tempo | written | means |
|---|---|---|
| steady and **vouched for**: any warp, or a reading whose windows agree within 0.02% | `150`, `128.02`, `119.50` | rounded to two decimals; `.00` is dropped, any other hundredths are kept |
| steady, a reading whose windows agree less tightly | `~128` | whole number, approximate |
| drifting (warp shape Drifting, or a reading that drifts) | `~96`, marked as drifting | whole numbers; the range on the song page |
| keytag's estimate | `~128`, labeled as keytag's | never exact |
| mostly steady: a warp with a main tempo and a ramp or drifting stretch besides | the main tempo, written as steady, with a note that a part changes | |
| changing: a warp with two or more stable tempos | the main tempo with a note that the track changes tempo | the detail is not needed |

Two decimals or none, never one: `119.50`, matching Ableton.

## Consequences

- **A bare number means the hundredths are known.** `128` on screen says the track is 128.00, not
  "about 128".
- **Most of the library shows its true hundredths for the first time**, and nearly a third of the
  steady warps are not whole numbers.
- **The four snapped warps in #91 show the reading's grid tempo, not their slope**, and the two
  typed ones keep the number the user typed, as declarations. Listening settles which is right.
- **The wording, chips and layout of each state are design's**, in brief 13. This ADR fixes what each
  state means, not how it looks.

## Amended 2026-10-02: a typed warp is not a declaration

#91 re-warped six tracks from scratch ([note](../notes/2026-10-02-the-six-warps-in-91.md)). Both
typed whole-number warps were wrong, most likely copied from crateside's whole-number BPM: Too $hort – *Blow
the Whistle* is 99.98, not 100, and White Shadow – *If You Like It* 127.98, not 128. So an exact warp gets no
special standing. It is held to the same snap as any other warp, which leaves every figure shown
unchanged: a warp that typed 128 still shows `128`, through the snap when the reading agrees and as
its own figure when it does not. The snap itself gave the settled figure on all six.
