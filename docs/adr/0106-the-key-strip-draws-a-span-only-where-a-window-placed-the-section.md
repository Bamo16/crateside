# 0106 - The key strip draws a span only where a window placed the section

- Status: Accepted
- Date: 2026-11-17
- Follows [ADR 0095](0095-a-second-key-centre-counts-only-when-something-independent-of-the-judge-places-it.md)
  (a second key centre needs independent support) and
  [ADR 0100](0100-a-key-lift-places-a-second-key-centre.md).

## Context

The key strip drew a band for a section that had only a start the judge gave, from that start to the
next boundary, with a dashed outline. A start the judge gave places nothing: it is the judge's
reading, not a measurement. The band still looked like a span the track had been checked against, so
a section shown with times read as more certain than it was.

## Decision

**A section is drawn on the strip only where a window placed it.** The band takes the window's span.
Every other section is named in the gutter with its key and the words `no window placed it`, whatever
start the judge gave it; the primary center fills the rest of the track as before.

The dashed band and its title `start not placed by a window or source` are gone, since nothing
unbacked is drawn any more.

## Consequences

- A song whose sections came only from Hooktheory or the judge shows one full-width primary band and
  a list of sections with their keys.
- Placing a section on the strip means asking for a window on it.
