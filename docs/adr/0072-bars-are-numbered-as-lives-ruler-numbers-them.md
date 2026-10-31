# 0072 — Bars are numbered as Live's ruler numbers them

- Status: Accepted
- Date: 2026-10-31
- Replaces the numbering in design handoffs 14 (the warp view: "bars are counted from the shared
  warp's bar 1 (the clip start)", the outside of the clip dimmed) and 18 (judge from these bars).

## Context

crateside numbered a warp's bars from the default clip's start: the ruler, the "Judge from these
bars" form, the clip's "starts at bar N", the zoomed view's `bars 61–68`, and the bars a typed range
was turned into seconds from. The form's help text said "Bars as Live counts them", but Live
numbers a warped clip's bars from the warp's beat 0, and the clip start is only a position on that
ruler: Live's Start field shows 22.3.4 for Eminem – *Without Me*.

**Measured** on 2026-10-31 through the API: VaVa, Snail's House – *Persona* (`kbV8MOB6mWY`) has one
marker, beat 2.5 at 0.3135 s, at 176.9953 BPM, so beat 0 is at −0.534 s. Its default clip starts at
44.213 s, beat 132, which Live shows as bar 34. crateside showed that start as bar 1. The
maintainer heard a modulation at Live's bar 106 (141.8 s) and typed `1-106`; crateside read it from
the clip start, 33 bars late.

**What the judge saw.** A window's seconds were computed from the clip-relative bars, so each
typed range was detected over the wrong stretch, late by the clip start. An unlabelled window was
also named by those bars in the judge's prompt (`"bars 1–106"` beside its seconds), and the
judge labels each section with its window's name.

The warp view also dimmed every lane before the clip start and after its end. For this maintainer
the clip start is a reference point, not the start of what matters.

## Decision

- **A bar is numbered as Live's ruler numbers it**: the warp's beat 0 is bar 1, and bars run over
  the whole file. One type, `BarRuler`, does the numbering for every place that shows, accepts or
  stores a bar.
- **Before beat 0 the bars are -1, -2…, with no bar 0**, and a position inside one reads `-1.2.1`.
  This is **a guess**, from how a pickup start is commonly written for Live (`-1.1.1`), not checked
  against Live's display. If Live is found to show a bar 0, `BarRuler.Number` and `BarRuler.Index` are
  the only place to change. A typed range takes negative bars (`-4–2`, `-4 -2`); `0` is refused.
- **A bar is as long as the warp's time signature says**, numerator × 4 / denominator of the
  warp's quarter-note beats (3 in 6/8), and Live's positions count the denominator's note as the
  beat. Tested on fixtures only; no warp in another signature was looked for in the library.
- **Seconds stay the truth.** Bars are read off the warp whenever a window is shown in the warp view,
  so a window recorded in seconds shows the bars it covers under the warp as it stands.
- **Stored bars that counted from the clip start are cleared**, by the ledger migration
  `LiveBarNumbers`, rather than shown or migrated: turning them into Live's bars needs each song's
  warp, which the ledger does not hold, and the seconds beside them already say where each window
  was. The ledger is copied to `ledger-backups/before-20261031183004_LiveBarNumbers.db` before the
  migration applies, and EF's history runs it once. **Measured** on a copy of the ledger that day:
  5 windows on 3 songs carried bars. Bars stored from now on are Live's.
- **The clip start is marked, never dimmed**: a notch and the label `clip start` on the ruler, and a
  1px line through each lane. Nothing before or after the clip is dimmed. The clip's detail reads
  `starts at bar 34`, or Live's `22.3.4` off a bar line.

## Consequences

- What the maintainer reads off Live's ruler can be typed as it is, and every number crateside
  shows can be found in Live at the same place.
- The judge's window names carry Live's bars, and its windows cover the stretch the maintainer
  meant.
- A run judged before this whose windows were given in bars shows its windows in time on the song
  page's Confidence line, and in Live's bars in the warp view. A section label the judge wrote from
  an old name (`Bars 1–64` on fusq – *Perfume!*) keeps its old numbers: it is the judge's text.
