# 0102 - Re-Pitch and Partners share one shift-range control

- Status: Accepted
- Date: 2026-11-17
- Implements [#169](https://github.com/Bamo16/crateside/issues/169) (Tools: one shift-range control
  for Re-Pitch and Partners), from design brief 25.

## Context

Re-Pitch limited its rows with a max-shift slider that was not in the URL and defaulted to ±3;
Partners used chips whose click meant a single shift or a span depending on the other chips. Two
controls for one idea, and a click whose effect hid in state.

## Decision

- **One `ShiftRange` component** (two handles on one track) serves both tools, backed by the pure
  `ShiftRange` record in `Crateside.Core/Pairing/`.
- **0 is always in range**: the low handle lives on −Max to 0, the high on 0 to +Max, because shift 0
  is the track as it is and both tools show it.
- **A drag, arrow key or track click moves one end only.** An optional `± linked` toggle, off by
  default, moves both ends together.
- **Re-Pitch and Partners keep separate ranges**: `rlo`/`rhi` and `lo`/`hi` in the URL, omitted at
  their defaults (Re-Pitch ±1, Partners 0 to 0). The chosen Re-Pitch shift widens its range.
- `PartnersTool.Click`'s single-then-span rule is gone; `RePitch.DefaultMaxShift` became
  `DefaultShift = 1`.

## Consequences

Old links keep their meaning: Re-Pitch had no range parameter, and `lo`/`hi` clamp as before. A span
wholly on one side (+1 to +3) can no longer be asked for.
