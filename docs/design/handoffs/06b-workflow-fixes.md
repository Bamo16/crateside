# Handoff: Claude Design ↔ Claude Code workflow fixes

## Overview
The Design session's review of `docs/agents/design.md` and `docs/design/DESIGN.md` at `ee7258c`,
done after brief 06. The round trip works. These changes remove the guessing and the duplicate
copies that brief 06 showed up. No UI is involved; this is only docs and process.

## Settle these first
Before making any change below, settle these three questions with the user or from the repo. Each
one changes what gets written.

1. **Where "differs from the design" notes live (item 2).** Option (a) is a section in
   `DESIGN.md`; option (b) is an `## As built` note appended to each handoff. The Design session
   recommends **(a)**, because the next session reads one file. Confirm (a) or (b) with the user.
2. **Brief 06's issue number.** This doc assumes **#21**, because brief 06 says "as last time"
   and follows from the #21 build. Check where the brief 06 result was actually posted, and use
   that number in item 1 and in `briefs/06-actions-layout.md`.
3. **Short issue comments (item 5).** This changes what the *user* does: they would paste a
   one-line pointer instead of a summary. Ask whether they want that. If not, drop item 5, and
   write that the handoff file wins where the issue comment and the handoff differ.

## Background: what the Design session can and cannot do
- It **can** read the repo (the local checkout and GitHub).
- It **cannot** write to the repo, and **cannot** read or write issues.
- So anything the next Design session needs to know has to be **committed**, not posted.

## Changes, in order of impact

### 1. Every brief names its issue
Brief 06 had no issue number, so the Design session had to guess (#21).
- Put this as the first line under the title of every `docs/design/briefs/NN-*.md`: `Issue: #NN`.
- Add the rule to `docs/agents/design.md` → "Out: the brief", step 1.

### 2. "Differs from the design" notes go in the repo as well as the issue
`design.md` says to post build differences on the issue (as #21 did). The Design session cannot
read issues, so the next brief starts from a canvas that no longer matches the screen.
- Keep the issue comment, and **also** commit the notes. Choose one place:
  - **(a)** a `## Differs from the design` section in `DESIGN.md`, grouped by page and cleared
    once a later design supersedes an item; or
  - **(b)** a `## As built` note appended to that brief's handoff file (this is the one exception
    to "never edit an old handoff").
- Also back-fill #21's existing "differs" comment into the chosen place.

### 3. A handoff takes its brief's number
`handoffs/` holds 01–04, the #21 handoff (brief 05) is missing, and brief 06's handoff is
`06-actions-layout.md`.
- Rule: `briefs/NN-<topic>.md` → `handoffs/NN-<topic>.md`.
- Back-fill the #21 handoff as `handoffs/05-song-page-issue-21.md`, from the #21 zip or comment.
- Renumbering 01–04 is not needed; they predate the briefs folder. Note the gap in `ARCHIVE.md`.

### 4. The zip is now partial
Until now the zip was the whole archive. From brief 06 on it holds **only what changed**: the new
handoff, any new or updated canvas, `support.js` if changed, and a `README.md` saying where each
file goes.
- Update the "Back: the result" section of `design.md` to say the zip may be full or partial,
  and that the zip's README says where each file goes.
- Keep the hash compare as a guard. With partial zips it should mostly find nothing to skip.

### 5. The issue comment points to the handoff and does not repeat it
Brief 06's comment summarized the handoff, which means two copies that can drift apart.
- From now on the comment is short: the brief number, "zip attached", and "build from
  `handoffs/NN-<topic>.md`". The handoff file is the source of truth.
- Add this to "Back: the result" in `design.md`.

### 6. Make the two "update DESIGN.md" rules match
- `DESIGN.md` header: *tokens, chips, header shapes or table caps*.
- `design.md` → "After building": *…table caps **or shared controls***.
- Use the longer list in both. "Shared controls" should cover the side stack (item 7).

### 7. After brief 06 is built: add a side stack section to DESIGN.md
Otherwise the next session will redraw the problem. Keep it short:
- **Side stack**: the Tracks row's right column and Track details' rail are the same stack, in
  this order: Confirm → Go → Next → Tags → Name → Uploads (rail only) → rule → Start over
  (Source, Remove). A group with nothing to show is not drawn.
- **Status + action**: a label line (label left, 1–2 buttons right), then the value, then the
  status as prose. A button never sits inside a sentence.
- **Tags**: settled = one line with Change; unsettled or Change pressed = every chip.
- **Destructive**: set apart by position and a stronger rule, not by color. Only the confirm
  step uses `.danger`.
- The `Part` / `ShowNextSentence` parameters, and the shared per-track edit state.

## Files to touch
- `docs/agents/design.md`: items 1, 2, 4, 5, 6
- `docs/design/DESIGN.md`: items 2a (if chosen), 6, 7
- `docs/design/ARCHIVE.md`: items 3, and index handoff 06 and its canvas
- `docs/design/handoffs/05-song-page-issue-21.md`: item 3 (new)
- `docs/design/briefs/06-actions-layout.md`: add `Issue: #21` (item 1)
