# crateside: Partners becomes a set of small mashup tools

Issue: #57 (with #58)

For a Claude Design session. Both issues are on the user's backburner: this is a design to have
ready, not something being built next. The implementing agent ports your design to Blazor, so send
layouts and states, not code.

Built against `e6c3802`. The rules as built are in `docs/design/DESIGN.md`. The page today is
`src/Crateside/Components/Pages/Partners.razor` (with its `.razor.css`). Your canvases are in
`docs/design/canvases/`.

---

## What Partners is today

A helper used on the spot while building a mashup: *what else can I use with this?* It returns keys
to filter for in Ableton's browser, never files.

- Controls: a **source key** (a select of the 24 keys, with Camelot codes) and a **max shift**
  slider in semitones.
- A summary line: how many of the 24 keys are reachable within that shift.
- A table, one row per reachable key: key, Camelot code, shift, alternative shifts, relationship.

## Where it is going (#57)

The user expects more small helpers like this, so the tab could become a page, or a group of pages,
of **small tools used on the spot**. The first new one is the Re-Pitch helper below. Still to
decide, and yours to propose:

- **Navigation:** one page with sections, sub-tabs under one tab, or something else. The tab row
  today: Tracks · Library · Partners · Settings.
- **Its name,** if "Partners" no longer covers it.
- **Whether a tool belongs inside Partners' table** rather than beside it (the user wondered this
  about Re-Pitch, below, without working it through).

## The first new tool: Re-Pitch (#58)

For nightcore and daycore edits, made in Ableton's **Re-Pitch** warp mode, where speed and pitch
move together. The user enters a track's BPM (and its key, to see where it lands), and the tool
gives the **arrangement BPM, to two decimals**, for each shift: −3, −2, −1, +1, +2, +3 semitones and
so on, with a slider like Partners' shift slider. It also shows the key each shift lands on.

The arithmetic: a shift of `n` semitones needs `arrangement BPM = track BPM × 2^(n/12)`. So 128 BPM
at +2 is 143.68, and at −1 is 120.82. The key moves by `n` semitones.

Two decimals here are the point of the tool (this is the value typed into Ableton), unlike
everywhere else in crateside, where BPMs show as whole numbers.

A possible link, for you to judge: warped tracks now have a true BPM (#68), so the BPM could be
picked from a track rather than typed. Draw it only if it earns its place.

## What to design

1. The tools page (or pages) with Partners and Re-Pitch, and how a third tool would join.
2. Re-Pitch's inputs and its result table, in both a typed-BPM state and an empty state.
3. If you think Re-Pitch belongs inside Partners' table, show that alternative as well.

## Constraints

- No new tokens, and nothing newly colored (`DESIGN.md`).
- Buttons are the existing `.outline` / `.primary` / `.danger`; the slider and select exist on
  Partners today.

## What to send back

A zip holding the handoff (`09-mashup-tools.md`), the canvas, `support.js` only if it changed, and a
README saying where each goes. The link route (`docs/agents/design.md`, handoff 06c) is preferred.
