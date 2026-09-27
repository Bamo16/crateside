# Handoff 09: Partners becomes Tools, and Re-Pitch

Issues: #57, #58 · answers `briefs/09-mashup-tools.md` · built against `e6c3802`
Canvas: `Crateside Tools Page.dc.html` (1a Partners in the new frame, 1b–1d Re-Pitch, 2a the
alternative).

The canvas is a design reference in HTML. Port it to Blazor. No new tokens, nothing newly
coloured, buttons stay `.outline` / `.primary` / `.danger`. Song names, Partners rows and picker
BPMs are illustrative; the Re-Pitch ladders are computed.

## 1. Name and navigation

- Tab label: **Tools**. Tab row: Tracks · Counts · Tools · Settings (Counts from handoff 08).
  `Bar` header shape, as Partners today.
- Page title block: **Tools**, subtitle "Small helpers for building a mashup, used on the spot."
- **Sub-tabs** under the title, one per tool: **Partners · Re-Pitch**. Plain text 14px/500,
  `--text-60`; the current one `--text` with a 2px `--text` underline sitting on the row's 1px
  `--line-strong` bottom rule. Gap 24px. This is deliberately unlike the header's filled tab so the
  two levels read apart.
- Each tool has its own route and `PageTitle`:
  `/tools/partners` (`Partners · crateside`), `/tools/repitch` (`Re-Pitch · crateside`).
  `/tools` goes to the last tool used (default Partners). Keep `/partners?key=` working as a
  redirect to `/tools/partners?key=`.
- **Shared key.** The key picked in one tool is the default in the other for the session (Partners'
  source key ↔ Re-Pitch's track key). Query strings: `?key=` on both, `?bpm=` on Re-Pitch.
- **A third tool** is one more sub-tab and route, with its own one-line lead, a controls box and a
  result. Same anatomy as the two here.
- Each tool body: a lead sentence (14px, `--text-75`, max 72ch) → the `.controls` box as built →
  a summary or hint line → the result table.

## 2. Partners (1a)

Unchanged, apart from moving its lead ("What else can I use with this?…") from the page subtitle
to the tool's lead line. Table cap `--table-partners`.

## 3. Re-Pitch (1b–1d)

### Controls (the existing `.controls` box)

1. **Track BPM**: a text input, 120px, mono 15px, accepts decimals (comma or point). Placeholder
   `e.g. 128`.
2. **Track key**, optional: Partners' select, plus a first option **No key**. Label adds
   "optional" in `--text-40`, no caps.
3. **Max shift**: Partners' slider, 1–12, default 3, label "±3 semitones".
4. **Or from a track** (right-aligned, `margin-left:auto`): a search field, dashed border when
   empty. See 1d.

### Result table (1b)

- Columns: **Shift** (90px, right, mono 13px `--text-50`) · **Arrangement BPM** (180px, right,
  mono 17px/500) · **Lands on** (140px) · **Camelot** (mono 13px `--text-50`). Cap 680px.
  Partners' table header style.
- Rows from `+max` down to `−max`, highest at the top, including **0**, the track itself, as
  the reference row: `rgba(255,255,255,.03)` wash, text `--text-60`.
- `arrangement BPM = track BPM × 2^(n/12)`, **two decimals always** (`128.00`, `143.68`,
  `120.82`). This is the one place in crateside that shows BPM decimals.
- Lands on: the key `n` semitones from the track key, same mode, named by `MusicKey.Name` as elsewhere (the canvas uses sharps for minor keys and flats for major; follow the code). Camelot follows it.
- **Click a BPM to copy it** (the digits only). The cell reads `copied` in `--text-75` for 1.5s,
  then returns. A hint line under the table says so, `--text-50` 13px.

### Empty and partial (1c)

- No BPM: hint line "Type a BPM, or pick a warped track." above the table; the table still draws
  every shift with em dashes (`--text-30`) in the other columns.
- BPM, no key: BPM column fills; Lands on and Camelot stay dashes.
- An unreadable BPM (not a number, ≤ 0, or > 999): the dashes stay and the field gets a red
  outline (`--red`, *needs attention*). No error sentence.

### From a track (1d)

- Why it's here: true BPMs (#68) are often non-whole (95.41), and everywhere else in crateside
  they're shown rounded, so the user can't read them off the Tracks page.
- Typing filters **warped tracks only** by song name. Up to 8 matches in a popover (`--raised`,
  1px `rgba(255,255,255,.12)`, radius 8px), each line: song (ellipsis) · true BPM to 2 decimals
  (mono) · Camelot (mono `--text-50`). Arrow keys + Enter, or click.
- Picking fills **Track BPM** from the warp and **Track key** from the Ableton key; both remain
  editable, and editing either leaves the picked name in the field as a reminder. An unsteady
  warp fills its median.

## 4. The alternative: inside Partners' table (2a) — not recommended now

An optional **Source BPM** field on Partners adds a **Re-Pitch BPM** column (replacing Score):
the arrangement BPM if the source is re-pitched onto each partner key. It answers *can I re-pitch
my way to that partner?*, but it only covers shifts that land on a partner and loses the plain ±n
ladder a nightcore edit starts from. Build Re-Pitch as its own tool (1b); keep 2a as a possible
later addition if the user asks.

## DESIGN.md

After building:
- **Page headers** table: Partners → **Tools · Bar**.
- A short **Tools** section: sub-tab row style, one route per tool, shared key, tool anatomy
  (lead → controls → hint → table), and that Re-Pitch is the one place BPMs show two decimals.
- `--table-partners` stays the cap for tool tables.
