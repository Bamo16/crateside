# crateside: one shift-range control for Re-Pitch and Partners

Issue: #169 (Tools: one shift-range control for Re-Pitch and Partners)

For an implementing agent; Bamo reviews first. The page's rules as built are in
`docs/design/DESIGN.md` (*Tools*, from line 563, and *Tools (handoff 12, #79)*, near line 892).

## Problem

Two controls on Tools limit pitch shifts, and for the same idea (which shifts are in play) they
look and behave nothing alike. From the issue: Re-Pitch's slider "shows seven rows when most work
happens within one semitone"; Partners' chips are "still confusing. What a click does depends on
the state of the other chips, and nothing on screen shows what the next click will do before it
happens." Settled in the issue: one control for both, no click whose effect depends on hidden
state, two handles on one track if a span is asymmetric, **Re-Pitch defaults to ±1**, both in the
URL, usable by keyboard with each end announcing its value.

## Current behavior

- Re-Pitch: `src/Crateside/Components/Tools/RePitchTool.razor:21` is a range input from 1 to
  `RePitch.MaxShift` (12, `src/Crateside.Core/Pairing/RePitch.cs:10`), default
  `RePitch.DefaultMaxShift` = 3 (:9); its rows run from `Max` down to `−Max`. `_rePitchMax`
  (`Pages/Tools.razor:102`) is **not in the URL**: `Query()` (:320) writes only track, key, bpm,
  `shift`, and `lo`/`hi`. The chosen shift is clamped at :204 and widens the range at :205.
- Partners: chips −`Partners.WidestShift` to +`WidestShift` (2, `Pairing/Partners.cs:13`) at
  `PartnersTool.razor:69-77`. `Click` (:187) makes one chip a single shift, or a second click a
  span, depending on whether `Lowest == Highest`. `lo`/`hi` are in the URL
  (`Tools.razor:125-129, 208-209`); `ChooseSpan` is at :277. The default span is "0 only"
  (`SpanText`, :181).
- Shift 0 ("As is") is always shown by Partners.

## Design

### The control: `ShiftRange`

One component, `src/Crateside/Components/Tools/ShiftRange.razor` (the Tools components are
already flat in `Tools/`), used by both tools: two handles on one track, so Partners' asymmetric
spans work and Re-Pitch behaves the same way.

```
 Shift range                              −1 to +1  ·  3 rows
 −2     −1      0      +1     +2          (Partners: five ticks)
  |      |      |      |      |
  ·──────●━━━━━━┿━━━━━━●──────·
         low    0      high

 −12 … −3  −2  −1   0  +1  +2  +3 … +12   (Re-Pitch: 25 ticks, same control)
```

- A track from −Max to +Max with a tick per semitone, **0 marked and fixed**, the bar filled
  between a low and a high handle. Re-Pitch: Max = 12 (`RePitch.MaxShift`). Partners: Max = 2
  (`Partners.WidestShift`).
- **The low handle lives on −Max to 0 and the high on 0 to +Max**; neither crosses 0, because
  shift 0 is the track as it is and both tools show it. **Confirm:** 0 is always in range.
  Alternative: allow a span wholly on one side (+1 to +3), as the old chips did.
- A readout above states the result: `−1 to +1 · 3 rows`, or `0 only`. It replaces Partners'
  `SpanText` and Re-Pitch's "Max shift ±N".
- **A drag or an arrow key moves one end and nothing else.** Clicking the track moves the nearer
  handle to that tick; no result depends on the other handle's state.
- A small `± linked` toggle, off by default, moves both ends together (Re-Pitch's old behaviour).
  **Confirm:** include it. Alternative: drop it; two drags is fine for ±1.
- Keyboard: each handle is `role="slider"` with `aria-valuemin`, `aria-valuemax`,
  `aria-valuenow`, and `aria-valuetext` "Lowest shift −1 semitone" / "Highest shift +1 semitone".
  Left/down and right/up move one semitone, Home/End go to the end of the track on that handle's
  side, PageUp/PageDown move 3. The focus ring uses the Tools accent.

### Defaults and state

| tool | default | URL, omitted when default |
|---|---|---|
| Re-Pitch | −1 to +1 | `rlo`, `rhi` |
| Partners | 0 to 0 | `lo`, `hi` (as now) |

**Confirm:** the two keep **separate** ranges, hence separate URL pairs. Alternative: one shared
range, which would show Partners shifts it was not asked for whenever Re-Pitch is at ±1.

The chosen Re-Pitch shift still widens the range to include it (`Tools.razor:205`). `_rePitchMax`
goes away; old links keep their meaning because Re-Pitch had no parameter to break.

### Logic, testable

A pure record `ShiftRange(int Lowest, int Highest, int Max)` in `Crateside.Core/Pairing/`:
`Rows()` (high to low, always including 0), `WithLowest`/`WithHighest` clamping to its side of 0
and to ±Max, `Includes(shift)`, and `Parse(lo, hi, max)` for the query, which clamps and never
throws on a reversed or out-of-range pair. `RePitch.DefaultMaxShift` becomes `DefaultShift = 1`.
Both tools draw rows from `Rows()`; `PartnersTool.Click` and its single-then-span rule are deleted.

## Test plan

- `ShiftRangeTests` in `tests/Crateside.Tests/Pairing/`: ±1 gives rows +1, 0, −1; the maximum
  gives 25 rows for Re-Pitch and 5 for Partners; 0..0 gives one; a low handle cannot pass 0;
  `lo=2&hi=-2` and `lo=99` clamp. `RePitchTests`: the default is ±1.
- On Tools with *Just a Friend* as the Working-on track (`/tools?track=…`):
  - Re-Pitch opens at ±1 with three rows; dragging the high handle to +3 gives five rows
    (+3 to −1) and the URL gains `rhi=3`; reloading restores it.
  - Partners opens at "0 only"; arrow-right on the high handle shows +1 and its partner group;
    `lo=-1&hi=1` in the URL reproduces the screenshot.
  - Tab to each handle and read its `aria-valuetext`.
  - At 400px wide, both handles stay reachable.

## Out of scope

Which keys Partners pairs; the Re-Pitch BPM maths; the Camelot wheel; a range beyond
`RePitch.MaxShift` or `Partners.WidestShift`; the track search and the key/BPM inputs.
