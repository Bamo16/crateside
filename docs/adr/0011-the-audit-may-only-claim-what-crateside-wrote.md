# 0011 — the audit may only claim what crateside wrote

- Status: Accepted
- Date: 2026-09-08
- Acts on [ADR 0003](0003-crateside-may-write-abletons-tag-store.md)'s finding of 2026-09-02,
  which called `TagAudit`'s findings over `Mashup Samples` "the least trustworthy of the lot"
  without saying what to do about it.

## Context

`TagAudit` was written to be library-wide: four kinds of finding — `Disagreements`, `Untagged`,
`PartialKeys`, `Inconsistencies` — over both stem roots. Only the first ever reached a row, and
the plan was to surface the rest as a *What is wrong* column with a fix verb per finding.

Run against the real library first, before building anything on it:

```
scanned=533  agreed=264  noVerdict=175  problems=94
disagreements=0  untagged=94  partial=0  inconsistent=0
```

**Every finding the audit has is `Untagged`, and every one is in `Mashup Samples`.** Staging, the
root crateside writes, reports nothing at all.

## What was measured

`Mashup Samples`' XMP was last written **2026-09-02 15:40** and holds 482 entries against 579
files. The split is exact and has no exceptions:

1. **93 of the 94 findings are files whose mtime is after that write.** The one that is not,
   `Snail's House - Grape Soda.flac` (2026-08-17), is the only genuine candidate in the set.
2. **Every library file newer than that write is absent from the XMP entirely** — 93 of 93. Not a
   keyless entry: no entry.
3. **Every entry that does exist carries a `Key|`** — 482 of 482. In this library there is no such
   thing as "indexed and untagged", so a missing key and a missing entry are the same observation.
4. **The mechanism, demonstrated on a live pair rather than inferred.**
   `S3RL - Elevate (feat. Little Sis Nora)` vocal and instrumental sit in `Mashup Samples` and the
   audit calls them untagged, while **staging's** XMP still carries their entries with `Key|D`,
   `Key|Minor` — the ledger's verdict, written there by crateside's tagging stage. The user dragged
   them out; Ableton took the tags into its own index and never wrote the library's store; staging
   kept the stale entries (ADR 0003, finding 15). The audit reads the gap as a missing tag.

So `Untagged` over `Mashup Samples` was measuring how long ago Ableton last flushed a mirror, not
what the library holds.

## Decision

**An absent key counts as a finding only in a root whose tag store crateside writes.**

`TagAudit.Run` takes `AuditRoot`s rather than paths, and each says which kind it is —
`AuditRoot.Written` for `Sample Staging`, where crateside's own write is the record and silence
therefore means untagged, and `AuditRoot.Mirrored` for `Mashup Samples`, where the store is a
lagging partial mirror of Ableton's index and silence means nobody can tell.

**Only `Untagged` changes.** `Disagreements`, `PartialKeys` and `Inconsistencies` all require a key
that is actually present in the store, so all three were already sound in either kind of root and
keep their reach across the whole library. The unsoundness was one rule, not the design.

`ProblemCount` over the real library goes **94 to 0**.

## Consequences

**The library is not audited, and the *What is wrong* column has nothing to draw.** Artboard 1c of
the design handoff is not buildable on this: its fix verbs would have offered **Retag** on 94 files
whose tags are fine, and the write would have gone into `Mashup Samples`, which ADR 0003 forbids.
Deferring the column is the point of this decision rather than a side effect of it.

**A library-wide audit needs Ableton's own index.** ADR 0003 established that the index, not the
per-root XMP, is the authoritative tag state, and left its location unchased. That is now the only
route to auditing the finished library, and it is a separate piece of work.

**The `Needs attention` count stops being a floor and becomes a count** — of nothing, today. Honest
emptiness is worth more than 94 findings the user would learn to scroll past, which is the failure
this avoids rather than merely a tidier number.

**`TaggingRunner` was never exposed.** It already scoped its work list to `paths.SampleStaging`, so
no tagging run was ever driven by a mirror artifact. The exposure was entirely in what the page was
about to show.

## Alternatives considered

**Chase Ableton's index first.** The only thing that makes item 1's original ambition real, and
still worth doing. Rejected as the immediate move because the unsound rule would have stayed in the
code for the length of an open-ended investigation, and because the rule is wrong on its own terms
regardless of what the investigation finds.

**Report the mirrored root's absences as a separate, softer finding** — "crateside cannot see these
tags". Rejected: it is a fact about crateside's reach, not about the library, and 93 rows of it
would crowd out the one real finding. If it is worth saying at all it is one sentence, not a list.

**Distinguish "entry present, no key" from "no entry" and keep the library in scope.** The
principled version, and it would have been the right shape if finding 3 had gone the other way. It
does not: there are no keyless entries in this library, so the distinction would separate 94
findings into 0 and 94 and change nothing. Not built, because a rule with no cases is a guess about
what Ableton might do rather than a reading of what it does.

## Amendment, 2026-09-09 — the index was chased down

[ADR 0013](0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md) locates and reads
Ableton's index, so the separate piece of work this ADR named is done and **a library-wide audit
is possible**.

What that changes here is narrower than it sounds. The decision above was sound on its evidence
and is not withdrawn: read from the XMP, `Mashup Samples` really is a mirror in which silence
means nobody can tell. What was wrong was the implied permanence. `AuditRoot.Mirrored` describes
**which store is stale**, not which root is auditable, and once the audit reads the index the
distinction stops applying to `Untagged` at all.

Measured against the index on the day of that ADR: `Mashup Samples` holds 579 files, 576 of them
keyed. The honest `Untagged` count over the library is **three**, all of them genuine and all
from June — not the 94 this ADR removed, and not the 0 it settled for.

Finding 3 above — "every entry that does exist carries a `Key|`" — was a fact about the XMP and
does not carry over: the index has entries and keys as separate things, and it happens to have no
keyless ones either, so the alternative this ADR rejected for having no cases still has none.

Nothing is changed in code by this amendment. `TagAudit` still reads the XMP, and switching it is
its own change.
