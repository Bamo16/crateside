# 0057 — The Design session commits its own result

- Status: Accepted
- Date: 2026-10-17
- Changes the round trip in [`docs/agents/design.md`](../agents/design.md), which handoffs 06b
  (the workflow review) and 06c (results by link) shaped.

## Context

The round trip was built on two facts about the Claude Design session: it can read the repo, and
it cannot write to it or see issues. So the brief went out through the repo, and the result came
back as a zip carried by the maintainer: a short-lived public link, a download, or a file attached
to an issue comment.

Brief 19 (#125) was the first given to Design started from Artifacts in a Claude Code cloud
session. That session reported, in `workflow-proposal.md` in its zip:

- **It reads issues and can comment on them.** It read #125 and commented there. MEASURED (the
  comment is on #125).
- **It cannot attach a file to a comment.** The GitHub tools it has take text only. Reported by
  the session, not checked here.
- **It has a clone of `master` with push credentials.** `git push --dry-run origin HEAD:master`
  succeeded. Nothing was pushed, so a real push is unexercised.
- **There is no short-lived public link.** Artifacts are private to the owner, and the session's
  network refused the file hosts it tried. Reported by the session.
- **The canvas is a Design artifact on claude.ai**, private and revisable in place; the repo copy
  is a snapshot. Brief 19's canvas renders with the repo's `support.js`, live logic included:
  MEASURED here on 2026-10-17, in headless Edge.

Everything the result adds to the repo is new or append-only: a handoff, a canvas, index lines in
`ARCHIVE.md`. The zip's README only ever told the code agent to copy those files, and the hash
compare guarded against a zip that carried the whole archive, which it has not since brief 06.

## Decision

**The Design session commits its result itself**, in one commit on `master`, and pushes:

- `docs/design/handoffs/NN-<topic>.md`, new, named after its brief;
- `docs/design/canvases/<Name>.dc.html`, new or replaced, the snapshot of the artifact;
- `docs/design/ARCHIVE.md`, its index lines and *superseded* marks on what it replaces;
- `docs/design/canvases/support.js`, only if the canvas cannot render without a newer one.

Then it comments on the issue: the commit, the handoff's path, and the artifact link. It touches
nothing else: `DESIGN.md` still changes only with the build, and `CONTEXT.md`, the ADRs, the briefs
and the code stay the code agent's.

**The code agent builds from that commit**, after a pull, and drops `needs-design`. Where it
disagrees with the design, the build records it under *Differs from the design*, as before.

**When a session cannot push, the zip is the fallback**: a download or a link the maintainer
passes on, with a README mapping it onto the same commit, which the code agent then makes. **An
issue attachment is no longer a route**, since no session can make one.

**Questions about a design go on the issue**, which outlives the session. An artifact's comments
reach its Design session only while that session is alive.

**Before relying on a new canvas, the code agent checks it renders** beside the repo's
`support.js`. If it does not, the build goes from the handoff and the artifact, and the issue says
so, so the next brief can ask for a canvas that renders.

**The brief stays self-contained.** It still names its issue and restates what the design needs,
since a session that cannot read issues must still be able to work from it.

## Considered and not taken

- **Keeping the maintainer as the carrier.** It costs them a step on every round trip and
  protects nothing: every file the result adds is one the code agent copied unread.
- **Letting the Design session update `DESIGN.md`.** It describes what is built, so it changes
  with the build; a design not yet built would make it wrong.

## Consequences

- The first result that comes back this way exercises the push. If it fails, the fallback is the
  route the earlier briefs used.
- A Design commit and a code commit can race on `ARCHIVE.md`. Each side rebases on a rejected
  push; the lines it adds are its own.
