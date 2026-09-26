# crateside: the expanded row's proportions, and genre from the collapsed row

Issue: #65

For the Claude Design session that drew the side stack (handoff 06). Two questions about the
Tracks row, one open and one closed. The implementing agent ports your design to Blazor, so send
layouts and states, not code.

Built against `c9c5e4b`. The rules as built are in `docs/design/DESIGN.md`, including its new
**Side stack** section. Your canvases are in `docs/design/canvases/`, and the newest is
`Crateside Actions Layout.dc.html`.

---

## 1. The open row: the samples column against the side stack

Handoff 06 is built. The stack reads well. What sits beside it does not. The user, 2026-09-25,
looking at a ready-to-warp row (`07-row-today.png`, beside this brief):

> the stack itself looks very nice, but [...] On a regular track, the picker is pretty short when
> not in change mode so it looks really sparse beside the stack. When expanded, it is quite large
> and commands a lot more space than the stack. Besides the visual size thing, it has the side
> effect of moving rows below it when expanding the list [...] if it's gonna be big then maybe the
> picker could be adjusted fit within that.

### What the left column holds (`StemList`)

Your canvas drew it as a "Samples" label line carrying three buttons, over a short list. The build
kept the list as it was (recorded under "Differs from the design" in `DESIGN.md`):

- **Closed:** a heading (`Stems · {folder} / {title}`), a column header (Sample · Ableton key ·
  Where it is), one boxed line per sample, and a full-width dashed **Change what is cut** opener
  with a hint on its right (*5 more presets, the source track*). A typical row has 2–4 samples.
  In the screenshot the column is about 230px tall against a stack of about 520px.
- **Open (picking):** every preset as a tickable line, in bands. Measured today: **9 presets** in
  three bands (Instrumental 4, Vocal 4, Karaoke 1), then **Sources** (the separation source, plus
  any added sources), then **Warp guide** (1), then **Add a source** and **Adopt a file** openers,
  then **Close**. About 15 lines and 5 band labels, taller than the stack.
- Opening *Add a source* or *Adopt a file* inserts a form (two fields, a row of type pills, a
  sentence, confirm/cancel) under the Sources band. The picker grows further.
- While picking after separation, a moved tick puts **StemConfirm** at the top of the stack and
  dims the rest (handoff 06, slot 0). Before separation, ticks apply at once and there is no
  confirm.
- **No samples yet** (a finding waiting, parked, not yet judged): a dashed placeholder box, from
  your canvas 1c.

### What seems wrong (confirm or overrule)

- **Closed, the list is short and the stack is tall.** The left column ends half-way down.
- **Open, the list is long and the stack sits unused beside it.** While a tick has moved, the stack
  is dimmed and inert, so its height buys nothing.
- **Expanding a row pushes every row below it down** by whichever column is taller. It always did,
  but the stack made the usual case taller.

### Ideas the user and the implementer had (not decisions)

- Fit the open picker within the stack's height, e.g. presets in two columns, or bands side by side.
- While picking, fold the stack to a thin summary, since it is inert then anyway.
- Give the closed list a job for its spare height, e.g. your "Samples" label line with the three
  acts, or more per sample.
- Cap the open panel's height, so expanding a row moves the rows below by a known amount.

### What to design

1. The open row's two columns in these states: closed with 2 samples; closed with 5; picking
   before separation (no confirm); picking after separation with the confirm open; picking with
   *Add a source* open; no samples yet.
2. Whether Track details' rail (19rem, beside samples, evidence and activity) needs the same
   change. There the left column is long, so it may not.

## 2. Genre from the collapsed row (carried over from #62)

The user asked for this on #62 after seeing the backlog. It never reached brief 06, so handoff 06
does not cover it.

Measured on 2026-09-25: **322 named songs** have no genre answer, **280** of them with a
suggestion. Genre learned per artist (ADR 0033) reaches only about 32 of them: only 5 artists have
4 or more songs. So the bulk is answered one song at a time. The fastest path is **accepting a
suggestion in one gesture from the collapsed row**, plus a way to reach the rest.

- The collapsed row's columns today: chevron · Song · Key · Confidence · BPM · State
  (`24px 1fr 90px 100px 80px 190px`, capped at `--table-tracks`). The Song cell already carries
  small flags (*renamed*, *added by URL*, *N files not renamed*).
- A suggestion is one of seven genres, or *none of these*, and is drawn today as an amber dashed
  chip (amber is "your turn"). Genres are listed in `GenreVocabulary`.
- Familiarity (Mainstream / Obscure) sits with genre in the stack's Tags group. Say whether it
  belongs in the collapsed row too. Measured on 2026-09-25, of 432 songs: 316 derive a value from
  the numbers, **115 ask the user** (no suggestion to accept), and 1 has been answered.
- Filters today: Unfinished · In staging · In flight · Needs attention · All. A *genre unanswered*
  view may belong with this.

### What to design

3. How a collapsed row offers its suggested genre, and what accepting it looks like.
4. How the user reaches songs with no suggestion, or a wrong one, without opening each row.

## Constraints

- No new tokens, and nothing newly coloured (`DESIGN.md`). Amber is still only *your turn*.
- Buttons are the existing `.outline` / `.primary` / `.danger`.
- `StemList`, `StemConfirm` and the side stack are each one component mounted in the row and on
  Track details. Say what differs by context, and the implementer passes it as a parameter.
- The side stack's order and patterns (handoff 06) stand unless you say otherwise.

## What to send back

A zip holding the handoff (`07-row-proportions.md`), the canvas, `support.js` only if it changed,
and a README saying where each goes. The link route (`docs/agents/design.md`, handoff 06c) is
preferred.
