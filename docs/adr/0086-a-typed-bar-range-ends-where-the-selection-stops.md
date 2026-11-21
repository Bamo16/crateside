# 0086 - a typed bar range ends where the selection stops

- Status: Accepted
- Date: 2026-11-08
- Amends [ADR 0072](0072-bars-are-numbered-as-lives-ruler-numbers-them.md) (how a range's end is
  read) and [ADR 0085](0085-a-track-or-a-stretch-of-it-can-be-marked-unfit-for-measuring-detectors.md)
  (the stretch input).

## Context

The maintainer selects bars in Live and reads the range off the status bar: `Start: 155.1.1
End: 156.3.1`. Live's end is a position, where the selection stops, so `65–66` is bar 65 alone.
crateside read the end bar as included (`33–48` ran to the end of bar 48), so a range read off Live
had to be typed one bar short.

## Decision

- **Every typed bar range is end-exclusive**, in the judge's "Judge from these bars" form and in
  the stretches of ADR 0085: `183–190` runs from the start of bar 183 to the start of bar 190, bars
  183 to 189. One type, `BarRanges`, reads and shows them.
- **Each side is a bar or Live's `bar.beat.sixteenth`** (`155.1.1–156.3.1`, `155–156.3.1`). `start`
  and `end` and the open forms (`106-end`, `106-`, `start-105`) stay.
- **A range is at least a sixteenth long.** `65–65` is refused with "the end is where the selection
  stops, as in Live: 65–66 is bar 65". A range that ends before it starts is simply unreadable.
- **Negative bars follow ADR 0072**: there is no bar 0, so `-1–1` is the bar before beat 0, from
  `-1.1.1` to `1.1.1`.
- **Ranges are shown back the same way**, from seconds through the warp, to the nearest sixteenth
  and with the end as Live would show it: `183–190`, `155.1.1–156.3.1` (a bar line shows as the bare
  bar, as in `Position`). The form's resolved line reads `bar 65`, `bar 26 to the end`, `the start to
  bar 10`, or `bars 183–190`. Dragging on the ruler writes the end bar after the last bar covered.
- **Two ranges that meet on one bar just meet** (`1-26`, `26-end`): the old rule that moved the
  first one's end back a bar is gone.

## Stored data

Nothing migrates; seconds are the truth. What the ledger holds in bars is `Windows.FirstBar` and
`LastBar`, the bars covered with both included, and that meaning is kept. They are converted for
display: `BarSpan.Text` shows `First` to the bar after `Last` (33 and 48 read `33–49`), which also
fixes the window name the judge is sent (`bars 82–97` for stored 82 and 96) and the Song page's
"judged from" line. Where the warp is at hand (the warp view, the retry form) the range is shown
from the seconds instead. A window now stores its bars only when it begins and ends on bar lines or
the file's edges; one typed with a beat (`155.2–156`) stores none and reads as a time on the Song
page. A section label the judge wrote from an old window name keeps its old numbers (it is the judge's
text, as in ADR 0072). A stretch's `typed` text is kept as it was typed and is not shown by the page,
which shows the positions recomputed from the seconds; a harness should use the seconds.

## Consequences

- What Live's status bar says can be typed as it is, in the form and for a stretch.
- The judge prompt names a bar window by its start and its stop (`bars 82–97`), not its last bar.
