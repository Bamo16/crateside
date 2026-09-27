# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

In this repo `ready-for-human` means **the next step is the user's**: a decision, an answer, or
hands-on work. An agent cannot advance it.

**Built work waiting for the user to look at it gets no label.** The user checks it in the session
that built it. If the session ends first, the check goes into the handoff for the next agent. The
issue stays open, with a comment saying what landed and that it has not been checked in the app.
**The agent closes it**, with a comment saying what finished it, as soon as the user has seen it
working or the work is verified without them (a real-data test, a check the user cannot do). The
user does not work the tracker. Drop `needs-triage` or `needs-design` once the work is underway.

**Sweep before a handoff.** Read every open issue against its last comment and the commits that
name it; close what is finished, and list what needs only something small. Finished work left open
for days has happened, and the user noticed it from their phone (2026-09-27).

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Labels outside the five roles

| Label | Meaning |
| ----- | ------- |
| `needs-design` | Waiting on a Claude Design session. Like `ready-for-human`, but the human's part is taking the brief to that session and bringing its result back to the issue as a comment with the files attached. Build from that comment, not before it. |
