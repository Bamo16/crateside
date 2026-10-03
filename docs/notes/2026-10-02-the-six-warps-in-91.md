# The six warps in #91, re-warped from scratch

*Note, 2026-10-02, frozen. For #91 and ADR 0049's nominal tempo.*

#91 listed six steady warps where the user's warp and crateside's reading disagreed. All six were
moved back to staging with their sidecars deleted, given fresh drum stems, and warped again by the
user's usual method. That method sets a marker on a clean transient near the start, then snaps one
near the end, without knowing the tempo.

## The results

| track | old warp | new warp | crateside's reading | settled |
|---|---|---|---|---|
| *Snail's House – Flash Drive* | 160.010 | 160 (typed: clearly 160) | 160.0006 | 160 |
| *Aiobahn – non-reflection* | 179.983 | 180.0015 | 179.9984 | 180 |
| *Princess Superstar – Perfect* | 119.512 | 119.509 | 119.4989 | 119.50 |
| *Lil Jon – Get Low* | 100.985 | 100.987 | 101.0015 | 101, with a slower ending |
| *Too $hort – Blow the Whistle* | 100, typed | 99.985 | 99.9817 | 99.98 |
| *White Shadow – If You Like It* | 128, typed | 127.984 | 127.9840 | 127.98 |

**How each was settled:** the drum stem's stronger transients were placed against a grid at each
candidate tempo. The grid's phase came from the first minute, and the offset was measured a minute
at a time. A wrong steady tempo shows as an offset that grows minute by minute. A section change
shows as a jump.

- *Blow the Whistle* at 100 grows 9 ms, then 18 ms; at 99.983 it stays under 1 ms. *If You Like
  It* at 128 grows 5, 10, 23, 26 ms; at 127.984 it stays within 5 ms.
- *Perfect* at 119.50 stays within 2 ms; the user's 119.509 reaches 9 ms.
- *Get Low* at 101 holds within ±6 ms for the first four and a half minutes, then lands 45 ms
  late in the last minute. The user's end-snap averages that ending in, and so does a single best
  fit (100.991).

## What it says

- **crateside's reading was right on all six**, for the body of each track, and ADR 0049's
  nominal rule gives the settled figure on all six.
- **A typed warp is not always a declaration.** Both typed warps were wrong, most likely because
  they were copied from crateside's whole-number BPM before it showed decimals. Across the library,
  9 of 121 typed warps with a tight reading disagree with it. Seven differ by 0.005–0.007 BPM,
  inside the reading's own noise. Two stand out: *The Chemical Brothers – The Salmon Dance* (124,
  reads 123.982) and *Ludacris – Southern Hospitality* (95, reads 94.992).
- **A single best fit over the transients is not a better judge than the reading.** It averages
  over a section change just as a start-and-end snap does. Measuring the offset minute by minute
  shows where the warp drifts.
