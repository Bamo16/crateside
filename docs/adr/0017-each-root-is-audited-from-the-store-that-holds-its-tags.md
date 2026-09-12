# 0017 — each root is audited from the store that holds its tags

- Status: Accepted
- Date: 2026-09-10
- Answers [#16](https://github.com/Bamo16/crateside/issues/16).
- Carries out the migration [ADR 0013](0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)
  deliberately did not do, and corrects a number both it and
  [ADR 0011](0011-the-audit-may-only-claim-what-crateside-wrote.md) carried.

## Context

ADR 0013 made Ableton's index readable and stopped there, on the grounds that switching the
readers "would have mixed a measurement with a migration". `TagAudit`, `StagingBoard` and the
tagging stage all still read the per-root XMP, which is a lagging mirror.

## Decision

**One fact decides the source: whether crateside writes that root's tag store.**

- **It does write `Sample Staging`'s XMP** (ADR 0003), so that is where its own work lands and
  the only place it can see it at once. Read the XMP.
- **It writes nothing else**, so everywhere else the XMP is a mirror Ableton maintains on its own
  schedule. Read the index, which is the tag state rather than a copy of it.

`AuditRoot.TagStoreIsWritten` already carried exactly that fact; it used to gate which findings
were trustworthy and now picks which store is read. That is what it was always really about, and
it is the one axis rather than two.

**`Untagged`'s root restriction is retired.** ADR 0011 limited it to roots crateside writes
because the XMP could not tell "untagged" from "not flushed yet". Each root is now read from the
store that actually holds its tags, so an absent key is a fact everywhere. `TaggedStem` loses
`TagStoreIsWritten` with it — `Untagged` was its only reader.

**`StagingBoard` and the tagging stage stay on the XMP, deliberately.** Both are read-after-write:
the tagging stage writes staging's XMP and then asks the audit what is still untagged, and the
board shows the user what was just tagged. Against the index both would lag by hours — the stage
would re-pick the same track until the index caught up. `TaggingSelector` gets this for free by
passing `AuditRoot.Written`, which is the same rule and not a special case.

## Consequences

**The library is auditable, and it audits clean.** Zero findings of any kind across 548 stems in
both roots.

> **Amended 2026-09-12.** It does not audit clean any more, and that is this decision working
> rather than failing. Two days later the audit reported **110 untagged** files in `Mashup
> Samples` — every one a file crateside had successfully tagged, whose Ableton keywords have since
> disappeared. Confirmed in the browser, not only in the stores. See
> [#24](https://github.com/Bamo16/crateside/issues/24).
>
> **These are genuine findings, and an earlier note calling them false was wrong.** The first
> guess was that the index had fallen behind and the audit was inventing findings — the thing
> ADR 0011 removed, re-created. Measurement killed that: the index holds all 610 file rows, the
> XMP has nothing the index lacks, and a WAL-replayed copy reads identically to the live
> connection. The keywords are simply gone.
>
> Read from the XMP, as before ADR 0017, this would have been invisible: `Untagged` was restricted
> to roots crateside writes precisely so the library could not be claimed about. Lifting that
> restriction is what surfaced 110 real losses on its first real outing.

**The audit has no screen, so its real-library test is its surface.** `TagAudit`'s only production
caller is `TaggingSelector`, which uses one reading of it — `Untagged` over staging. Everything
else the report computes is exercised only by tests. The clean assertion is written so that a
failure means a real finding to go and look at rather than a test to relax; whether the report
deserves a screen is a separate question this does not answer.

## What was measured, 2026-09-10

Re-measured before building, because #16's numbers were a day old and this repo has twice written
an unchecked premise into the correction of another one.

```
Mashup Samples              issue (09-09)      today
  stem files on disk                 579        591
  keyed via the index                576        588
  keyed via the XMP                  482        480
  index only                          94        108
  XMP only                             0          0
  both present, keys differ            0          0

Sample Staging: 21 files, 21 keyed via each, no divergence.
```

**The gap widened rather than closed.** The library's XMP was rewritten by Ableton at 19:43 that
same day and is still 108 files behind — it is not converging, which is the whole reason to read
the index.

**Staging shows no divergence at all**, which is what makes keeping it on the XMP cost nothing:
the XMP is not behind there, it is merely the store crateside can see its own writes in.

## The correction: "three" was never a finding count

ADR 0013 wrote that "the honest finding count over `Mashup Samples` is three", ADR 0011's
amendment repeated it, and #16 repeated it again. It is wrong, and the audit reports **zero**.

Three is the number of library files the index does not key. `Untagged` requires an absent key
**and a taggable verdict**, and all three are unjudged — `Delta Heavy - White Flag` (both stems,
`wpf8JgkOrnQ`) and `Kreayshawn - Gucci Gucci (Acapella)` (`glF85prh3e4`). The ledger has never
seen either video, so they are counted by `NoVerdictCount` (175 of 548) and cannot reach
`Untagged` by any route.

The mistake is the one CLAUDE.md names: a coverage number from one table was reused as a finding
count in another without being run. Both ADRs are amended in place.
