# 0036 — fonts are self-hosted IBM Plex, Latin subsets only

- Status: Accepted
- Date: 2026-09-03 (recorded as an ADR on 2026-09-26, from `docs/fonts.md`, which held it until then)

## Decision

- **IBM Plex Sans** for text, **IBM Plex Mono** for every number, key, timestamp and count. Those
  are read as values and compared down a column, which proportional digits defeat.
- **Self-hosted**, not linked from Google Fonts. The bench is a local tool that should not need the
  internet to render correctly, and a third-party request on every page load buys nothing here.
- **The `latin` and `latin-ext` subsets only.** Google serves six; the other four are Cyrillic,
  Greek and Vietnamese. Twelve files, about 309 KB. IBM Plex has no CJK coverage at all, so a track
  like `電音部, Moe Shop - Eat Sleep Dance` falls back to the system font for its Japanese. That is
  expected and not worth solving, since the fallback renders it correctly.
- **Weights 400, 500 and 600**, for both families: 600 for the brand and for BPM, 500 for row text
  and labels, 400 for prose. Adding a weight means adding four files, so it is worth being
  deliberate about.

## The sharp and flat signs fall back, on purpose

Keys are spelled with **♯** (U+266F) and **♭** (U+266D), matching Ableton's own tag vocabulary.
Neither codepoint is in the `latin` or `latin-ext` subsets, so the browser renders those two glyphs
from a system font rather than from Plex Mono.

IBM Plex Mono **does** contain them. Google will describe a two-glyph subset if asked with
`&text=♯♭`, but the `fonts.gstatic.com/l/font?kit=…` URL it hands back returns **400** when fetched
outside the browser request that produced it, with or without a browser user agent. The remaining
way to get them is the full face from the `@ibm/plex` package, which is about 100 KB per weight for
two characters.

Left as a fallback because the cost is smaller than it first looks: the Key column is left-aligned
text, so a glyph of a slightly different width shifts nothing. Tabular alignment only matters in the
BPM column, which is digits, and those come from the subsets that are loaded. If the mismatch ever
looks wrong on screen, the fix is the full face rather than another attempt at the subsetter.
