# 0019 — a track has many sources, and one of them is the separation source

- Status: Accepted
- Date: 2026-09-12
- Answers the added-source half of [#22](https://github.com/Bamo16/crateside/issues/22).
- Amends [ADR 0003](0003-crateside-may-write-abletons-tag-store.md) and
  [ADR 0005](0005-the-stages-are-ordered-and-crateside-holds-the-working-set.md); supersedes
  neither.

## Context

The picker made a track's files choosable ([ADR 0018](0018-the-plan-is-the-record-and-the-warp-guide-is-not-part-of-it.md)).
What it cannot yet offer is a file crateside did not make: an official instrumental that beats
the separated stem, an acapella, a better master. The design brief called these *additional
sources* in one place and *other uploads* in another, and neither word survived contact —
*upload* is YouTube-shaped and local files are coming, and *source* was already carrying four
jobs.

## Decision

**A source is a whole file of a track's audio, as against a stem cut out of one.** A track has
as many as the user gives it and each is a sample in its own right. Two roles attach to
particular ones:

- the **separation source** — the one the stems are cut from, and the one *Swap source* changes
- the **detection source** — the one the evidence run measured

They are usually the same file and need not be: a swap moves the first and leaves the second
where it was, which is already true of *Ludacris, Pharrell — Southern Hospitality*. Everything
else is an **added source**.

*Whole* was considered and rejected as the origin word. It would have read correctly and cost a
rename of `KeepSource`, `StatedSourceUrl` and `StemKind.Source` — or, without one, a picker whose
band says one word while the model underneath says another. *Version* was rejected because
keytag already spends it on **canonical version**, meaning one upload of the same master, which
an official instrumental is not.

**The picker locks actions, not rows.** It used to go inert the moment any sample reached
`Mashup Samples`, which forbade adding to a finished track — so adding a source to Superbeast
meant moving its stems back to staging first, ceremony that deleted nothing and protected
nothing. Only one action is genuinely impossible, and only that one is blocked: **unticking a
file that has left for `Mashup Samples`**. Ticking an unmade preset and adding a source write to
`Sample Staging` alone.

**crateside does not delete from `Mashup Samples`.** Written down here for the first time. The
code cited ADR 0003 for it, and ADR 0003 does not say it — that ADR is about the *tag store*, and
its clause is "`Sample Staging` only, never `Mashup Samples`" for writing tags. The rule about
files is separate and has its own reason: those are warped files with `.asd` sidecars beside
them, and deleting one destroys work crateside never did and cannot redo.

**Downloading becomes a stage of its own**, serving the detection source before judging and an
added source at any time. Both are `stemforge-cli download`; they differ in where the bytes land
— the working set for the first, the stems output for the second — and in whether anything waits
on them. The evidence run becomes what its name says: run the detectors over audio already held.

ADR 0005 §4 survives intact. Its two load-bearing halves are *one download per track* and *it
must be StemForge's* — the second because a file acquired any other way carries no provenance and
every stem loses its join key. Neither requires the call to sit inside the evidence run, and the
split formalizes what is already true: `AcquireAsync` returns early when the working set holds
the file, so a download that succeeded and a detection that failed already behave as two steps
with a durable intermediate.

**A premium shortfall on an added source is a finding on that source, not a park for the track.**
ADR 0016 parks a track whose *detection* source came back short, before the verdict, because the
way out is a better upload. Nothing downstream depends on an added source, and the user named
that URL deliberately — there may be no other official instrumental. The quality is reported on
the line where the file is, and what to do about it stays theirs.

**Nothing downstream needs building.** `HandoffSelector` enumerates the stems output and moves
anything carrying a `SourceVideoId`; `TaggingSelector` audits `Sample Staging` and tags anything
untagged with a verdict. A downloaded file with provenance is staged, renamed and key-tagged with
no new machinery, which is why the whole of this is a download and a restatement.

## Consequences

**An added source must be restated in keytag before it is fetched.** The row groups uploads by
what the ledger says is the same song, so without it the file appears as a row of its own rather
than as part of the track. `TrackSwap` already establishes the order — restate first, then work.

**The label is required and unique per track.** It becomes the filename suffix, because
`StemFileName.WithoutPreset` strips a trailing parenthesized group and a file without one reads
as its whole name being the track's name. Two added sources sharing a label collide in staging,
which `HandoffRunner` fails loudly rather than silently.

**A finished row can be reopened by one tick.** `TrackRow.State` already takes a row out of *done*
when a sample is not in the library, so this needs no mechanism — only the per-line lock that
stops forbidding it.

**Local files remain unsolved and out of scope.** Every reader joins on the provenance
`stemforge-cli download` writes, and a local file has none. Two files in the library are already
in that state — `SUPADOPA (Official Instrumental)` and `The Mandalorian` — and they are what the
problem looks like.

## The kept source is the download stage's too — 2026-09-13

This ADR gave the download stage two kinds of work: the detection source before judging, and an
added source at any time. It has three. **Copying the separation source into the destination, for
a plan that keeps it, belongs here as well** — and it did not move when the stage was first split
out, because `--keep-source` was a flag on `stemforge-cli separate` and the copy had grown up
inside `SeparationRunner`.

The cost of leaving it there was visible rather than theoretical: pausing separation stopped a file
copy, and a track whose only outstanding item was its source sat at *judged · in line* waiting for
a GPU it was never going to reach. **A recipe with no passes now keeps a track out of the
separation list entirely**, since the separator has nothing to do for it.

The same move took the URL fallback with it. The audio branch reads both work lists, so a re-cut
track with an empty working set is fetched here rather than by handing the URL to the separator —
acquisition inside the stage that separates, which is the same confusion one level down.
