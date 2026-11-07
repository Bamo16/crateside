# 0072 — Bars are numbered as Live's ruler numbers them

- Status: Accepted
- Date: 2026-10-31
- Replaces the numbering in design handoffs 14 (the warp view: "bars are counted from the shared
  warp's bar 1 (the clip start)", the outside of the clip dimmed) and 18 (judge from these bars).

## Context

Live numbers a warped clip's bars from the warp's beat 0; the default clip's start is only a
position on that ruler (Eminem – *Without Me*'s Start field reads 22.3.4). crateside numbered bars
from the clip start instead, in the ruler, the "Judge from these bars" form, the clip's "starts at
bar N" and the zoomed view's bar label, and turned typed ranges into seconds from the clip start.
**Measured** on VaVa, Snail's House – *Persona* (`kbV8MOB6mWY`): beat 0 is at −0.534 s and the
clip starts at bar 34, which crateside showed as bar 1, so a typed range landed 33 bars late. The
judge was given those stretches, and an unlabelled window's clip-relative bars as its name.

## Decision

- **A bar is numbered as Live's ruler numbers it**: the warp's beat 0 is bar 1, and bars run over
  the whole file. One type, `BarRuler`, does the numbering for every place that shows, accepts or
  stores a bar.
- **Before beat 0 the bars are -1, -2…, with no bar 0**, and a position inside one reads `-1.3.1`.
  **Seen** in Live 12 on 2026-10-31: the ruler runs …, -2, -1, 1, 2, and the insert marker reads
  `Insert Mark -1.3.1`. A typed range takes negative bars (`-4–2`, `-4 -2`); `0` is refused.
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
  1px line through each lane. Nothing before or after the clip is dimmed; the clip start is a
  reference point, not the start of what matters. The clip's detail reads
  `starts at bar 34`, or Live's `22.3.4` off a bar line.
- **Ranges may be open and may meet** (#173): `start-105`, `106-end` and `106-` run to the file's
  edges, and a bar past the end is clamped to it. Two ranges that meet on one bar (`1-106`,
  `106-end`) read as a change at the start of that bar, so the first ends where the second begins.
  Each row of the form shows the stretch it resolves to.

## Consequences

- What the maintainer reads off Live's ruler can be typed as it is, and every number crateside
  shows can be found in Live at the same place.
- The judge's window names carry Live's bars, and its windows cover the stretch the maintainer
  meant.
- A run judged before this whose windows were given in bars shows its windows in time on the song
  page's Confidence line, and in Live's bars in the warp view. A section label the judge wrote from
  an old name (`Bars 1–64` on fusq – *Perfume!*) keeps its old numbers: it is the judge's text.
