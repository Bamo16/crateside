# Design work: the round trip with Claude Design

Page design is done in a separate **Claude Design** session at `claude.ai/design`, not here. What
that session can do decides the whole workflow:

- **It can read this repo**, both the local checkout and GitHub.
- **It cannot write to the repo, and cannot read or write issues.**

So the brief goes out through the repo, and the result comes back through the user.

## Out: the brief

1. Write the brief to `docs/design/briefs/NN-<topic>.md`, numbered after the last one, and
   **commit and push it**. That is how the Design session reads it.
2. Point it at what already exists: `docs/design/DESIGN.md` (the rules as built) and the canvases
   in `docs/design/canvases/`.
3. Track the work on an issue labelled `needs-design`, linking the brief. Do not build before the
   result arrives.

## Back: the result

The user posts the Design session's output **as a comment on the issue, with a zip attached**.
Fetch the attachment with the `gh` token, since attachment URLs need auth:

```
curl -sL -H "Authorization: token $(gh auth token)" -o design.zip "<attachment url>"
```

Unzip it in the scratchpad, not the repo. The zip has so far been **the whole design archive**:
`canvases/`, `handoffs/`, `brand/`, `briefs/` and a `README.md`. So it overlaps what is already
committed.

## What to commit from it

`docs/design/` holds history plus one living file. Of the zip:

| from the zip | goes to | rule |
|---|---|---|
| a canvas that is new or has new turns | `docs/design/canvases/` (replace the file) | canvases accumulate turns, so the newest file supersedes |
| a new handoff | `docs/design/handoffs/NN-<topic>.md` | new file; never edit an old one |
| `README.md` | merge its index into `docs/design/ARCHIVE.md` | the zip calls it README; the repo calls it ARCHIVE |
| `brand/`, `briefs/`, `support.js` | only if changed | the briefs originate here, so theirs are copies |

Anything unchanged is left alone. Compare before copying (a hash is enough) so the commit shows
only what the round trip actually changed.

## After building

Update `docs/design/DESIGN.md` in the same commit as any change to tokens, chips, header shapes,
table caps or shared controls. It is what the next Design session starts from.

Where the build departs from the design, say so on the issue, as #21's "differs from the design"
comment did, so the next brief starts from what is actually on screen.
