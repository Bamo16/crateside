# 0052 — crateside corrects a library key to its verdict

- Status: Accepted
- Date: 2026-10-07
- Supersedes the last write restriction of [ADR 0003](0003-crateside-may-write-abletons-tag-store.md),
  as [ADR 0023](0023-a-key-with-no-store-entry-is-not-durable.md) left it: that the backing pass
  never rewrites a key already in the library's store.

## Context

ADR 0003 kept crateside out of the library's tag store because the library "holds hand-applied
tags crateside knows nothing about". ADR 0023 let crateside back files there, then genre and
familiarity followed, so the store is written all the time. The key is the only tag crateside
still never changes there. Backing copies whatever key the index holds.

That leaves no way to settle a disagreement. ADR 0035 predicted the case: a library backfill
verdict that disagrees with a key tagged by hand before crateside existed. Measured on 2026-10-07,
16 library files (8 songs) carry a key that is not their verdict. All eight were downloaded
for evidence on 2026-09-25, judged on 2026-10-04 to 10-06, and never separated. They are backfill
tracks whose stems are older than crateside, not files tagged after a verdict. Five of the eight
tags are the verdict's relative or parallel key.

The premise behind 0003's rule no longer holds. The user does not tag by hand in either root, and
has said that for a track added today they would trust crateside's verdict over their old
process. Nearly every tag in the library is now crateside's: key, Type, genre, familiarity. The
user wants to keep the option of an arbitrary tag of their own on a sample.

## Decision

**crateside may rewrite the `Key|` group of a library file to the file's current verdict, for
files the user has approved.**

- **One write per root, as backing is.** A correction rides the backing pass's single `Apply`.
  A separate write would reconcile the root and destroy every key not yet backed (ADR 0023).
- **crateside's own groups only.** A correction replaces `Key|`. Genre and familiarity are already
  replaced by backing. Every other keyword, including any the user adds by hand, is kept as found,
  and a hidden keyword is never written back.
- **Approved by the user first.** crateside lists each file whose key differs from its verdict,
  with both keys, and writes only what the user confirms. Correcting on its own is a later
  decision, to be taken once the list has been seen to be right.
- **A backup before every write to the library's store**, kept beside crateside's other backups.
  Losing the store is recoverable without one, since crateside can retag the library from the
  ledger and its own records, but a backup makes one bad write cheap to undo.
- **The disagreement is shown on the dashboard** with the library's other counts. It recurs:
  every library backfill verdict can produce one, and so can a re-judge that supersedes a verdict.

Words: this ADR says *the library* for the root ADR 0003 called `Mashup Samples`, as
[CONTEXT.md](../../CONTEXT.md) does.

## Consequences

- `The_index_and_the_ledger_never_disagree_about_a_key` can be made to pass by the user, not only
  by re-tagging in Ableton.
- The rule that has not moved: crateside never deletes a file from the library (ADR 0019).
- A user's own key on a library file is not protected. A file the user wants keyed against its
  verdict needs the verdict superseded, which is keytag's business.

**Amended 2026-11-01 ([#175](https://github.com/Bamo16/crateside/issues/175), a sample's Key tag
always follows crateside's recorded key): the approval is gone, and so is the restriction to the
library.** A Key tag that differs from its song's taggable verdict means "not yet written", never
"the user's choice", the way loudness gain does ([ADR 0059](0059-a-sample-arrives-at-one-loudness.md)).
Every backing pass rewrites it in Staging and the library alike, in the pass's single `Apply`, and
logs `retagged <file>: <old> → <new>`. The rest of the Decision stands: only `Key|` is replaced, other
keywords are kept, and the library's store is backed up before a write. The Dashboard's "Keys behind
their verdict" band and `key-corrections.json` are removed. A manual key override is not built; when it
is, it becomes the recorded key and is written the same way.

Why *Persona* ([kbV8MOB6mWY](https://music.youtube.com/watch?v=kbV8MOB6mWY)) was not retagged,
read from the code: its stems were in Staging, where the backing pass ran with `backKeys: false`
and the tagging stage (ADR 0017) writes a key only to a file that has none, so a differing key was
never looked at; and the correction that did exist ran in the library only, on files the user had
approved. Nothing else treated a differing tag as the user's: the audit and the song page only
report it.
