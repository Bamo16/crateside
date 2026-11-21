# crateside: Partners and Re-Pitch on one page, sharing a selected track

Issue: #79

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against `9077ac3`. The rules as built are in `docs/design/DESIGN.md`. Handoff 09
(`docs/design/handoffs/09-mashup-tools.md`) is what was built. The pages today are
`src/Crateside/Components/Pages/Partners.razor` and `RePitch.razor`, under
`Components/ToolsFrame.razor`. Your canvases are in `docs/design/canvases/`.

---

## Where it stands

Handoff 09 is built: **Tools**, with sub-tabs Partners · Re-Pitch, a route each, and the source
key shared between them. The user has used it and likes it. Two things added in the build went
down well and should stay: the **search over warped tracks** that fills Re-Pitch's BPM and key, and
**click to copy** on an arrangement BPM. He pastes values into tools like After Effects. Ableton
itself will not take a pasted number, so there the value is read and typed.

## What the user found, in his words, condensed

He is making a mashup with *Xi – Ascension to Heaven*, **200 BPM, B minor**, as the instrumental.
It sounds good slowed down in Re-Pitch, so he tries pairings at one shift after another until it
sounds *too* weird.

1. He opens Re-Pitch, types "asc" in *From a track*, and picks it. The key becomes B minor and the
   BPM 200.
2. The table reads **−1 · 188.77 · A♯ minor · 3A**. He tries pairings in A♯ minor, then in C♯
   major (the relative major). Nothing fits.
3. Beyond relative keys, he can't remember the key relationships, so he turns to Partners. **But
   Partners still says B minor**, the track's own key, not the A♯ minor he is working in. He has to
   remember what Re-Pitch said, or go back to it.

## What he wants

- **Both tools on one page, visible at once.** Re-Pitch can **collapse**, since it is used less.
- **A selected track, shared by both tools.** Picking a track selects it for the page, not only
  for Re-Pitch. It is shown with its details (name, key, warped BPM) and is **interactive**:
  clicking it picks a different track. A key and BPM typed by hand, with no track, still work.
- **The picker sits on the left with the tables**, not across the page at the far right edge
  (where it lands today on a wide window).
- **The shift carries into Partners.** Choosing a shift in Re-Pitch (−1) makes its landing key
  (A♯ minor) the key Partners works from. Make it clear which key Partners is using: the track's
  own, or the shifted one, and by how much.
- **Other ways of organizing or navigating the keys**, if you see one. He can't recall
  relationships beyond relative major and minor, which is what Partners is for. A Camelot wheel,
  grouping by relationship, or switching to the relative key in one click are all fair game.
  Propose what serves the scenario above.

## The viewport

A **maximized Chrome window on a 1440p or 4K monitor** is the norm. Today the Re-Pitch controls
spread across the full width with the track search alone at the far right. Lay the page out for
the wide case, and let it fold to a single column when narrow.

## What to design

1. The combined page: the selected track, Partners, and Re-Pitch (open and collapsed).
2. States: no track and no key; a key typed with no BPM; a track selected; a shift chosen, with
   Partners following it.
3. How a third tool would join the page later.

## Constraints

- No new tokens, and nothing newly colored (`DESIGN.md`).
- Two decimals for arrangement BPMs (the point of Re-Pitch); whole numbers everywhere else.
- Buttons are `.outline` / `.primary` / `.danger`. The slider, select and pills exist already.
- The routes `/tools/partners` and `/tools/repitch` exist and can change. Say what they become.

## What to send back

A zip holding the handoff (`12-tools-together.md`), the canvas, `support.js` only if it changed,
and a README saying where each goes. The link route (`docs/agents/design.md`, handoff 06c) is
preferred.
