# Design work: the round trip with Claude Design

Page design is done in a separate **Claude Design** session, not here. What that session can do
decides the workflow ([ADR 0057](../adr/0057-the-design-session-commits-its-own-result.md)):

- **It reads this repo and its issues**, and can comment on an issue.
- **It can commit to `master` and push.** An older Claude Design session, or a restricted one,
  may not; the fallback below covers it.
- **It cannot attach a file to an issue comment.**

So the brief goes out through the repo, and the result comes back as a commit.

## Out: the brief

1. Write the brief to `docs/design/briefs/NN-<topic>.md`, numbered after the last one, with
   `Issue: #NN` as the first line under its title, and **commit and push it**. Keep it
   self-contained: restate what the design needs rather than pointing at the issue, so a session
   that cannot read issues can still work from it.
2. Point it at what already exists: `docs/design/DESIGN.md` (the rules as built) and the canvases
   in `docs/design/canvases/`.
3. Track the work on an issue labelled `needs-design`, linking the brief. Do not build before the
   result arrives.

## Back: the result

**The Design session commits it**, in one commit on `master`, then comments on the issue with
the commit, the handoff's path and the artifact link. The commit holds:

| file | rule |
|---|---|
| `docs/design/handoffs/NN-<topic>.md` | new, named after its brief (`briefs/06-x.md` → `handoffs/06-x.md`); never edit an old one |
| `docs/design/canvases/<Name>.dc.html` | new, or replaced when it has new turns: the snapshot of the Design artifact |
| `docs/design/ARCHIVE.md` | the index lines for both, and *superseded* marks on what they replace |
| `docs/design/canvases/support.js` | only if the canvas cannot render with the one there |

It touches nothing else. `DESIGN.md` changes only with the build; `CONTEXT.md`, the ADRs, the
briefs and the code are the code agent's.

To build: **pull**, drop `needs-design`, and build from the handoff in that commit. Ask any
question about the design on the issue, not in the artifact's comments, which reach the Design
session only while it is alive.

### The fallback: a zip

When the session cannot push, it sends a zip to the user with a `README.md` saying where each file
goes. It arrives as **a download** in the user's Downloads folder, named `crateside-design-NN*`
(take the newest match for the brief's number; a `-vN` suffix beats none), or as **a link the user
pastes**, short-lived and needing no auth: fetch it at once with `curl -sL -o design.zip "<url>"`.

Unzip it in the scratchpad, not the repo, and make the commit the table above describes: the
files where the README says, and its index lines merged into `ARCHIVE.md` (the zip calls it
README; the repo calls it ARCHIVE). Compare before copying anything that already exists (a hash is
enough), so the commit shows only what changed. Then say on the issue that the result arrived and
what was committed.

## Check the canvas renders

Before relying on a new canvas, open it beside the repo's `support.js`. A headless screenshot is
enough: copy the canvas and `support.js` into one scratch folder and run Chrome or Edge with
`--headless=new --allow-file-access-from-files --virtual-time-budget=8000 --screenshot=<png>` on
the file. If it does not render, build from the handoff and the artifact, and say so on the issue.

## After building

Update `docs/design/DESIGN.md` in the same commit as any change to tokens, chips, header shapes,
table caps or shared controls. It is what the next Design session starts from.

Where the build departs from the design, **record it under "Differs from the design" in
`DESIGN.md`**, by page, as well as on the issue, so the next brief does not start from a canvas
the screen no longer matches. Clear an entry once a later design supersedes it.
