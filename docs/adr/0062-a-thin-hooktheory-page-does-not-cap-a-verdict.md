# 0062 — A thin Hooktheory page does not cap a verdict

- Status: Accepted
- Date: 2026-10-21
- Amends the first fixed rule of [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md).
  Builds [#122](https://github.com/Bamo16/crateside/issues/122) on
  [#124](https://github.com/Bamo16/crateside/issues/124).

## Context

Rule 1 of the judge's prompt caps a verdict at MEDIUM whenever an authoritative source names
another key. A TheoryTab is authoritative however little it covers. **MEASURED**
([the count](../notes/2026-10-20-how-thin-the-hooktheory-sources-are.md), 184 pages): 34 have one
contributor, 98 map one or two sections, and the median page covers 20% of the recording. One
person's analysis of one section can therefore hold a track at MEDIUM against everything else.

## Decision

- **A thin page is one contributor and one or two sections**, read from the facts #124 gathers
  (`HooktheoryTab.IsThin`). Contributors leave out the site's `Hooktheory` account and
  `[deleted user]`, so a page with none named is not thin. By the count this is 30 pages and 33
  Authoritative source rows.
- **The work order marks it** with a `THIN PAGE` line under the page, beside its contributors,
  sections and coverage.
- **Rule 1 does not apply to a marked page.** The judge still sees it, records it, and may reach
  MEDIUM on its own reasoning; it is just not a mandatory cap. Musicnotes and Ultimate Guitar keep
  Rule 1 as it was.
- **Coverage is not part of the definition.** 65% of pages cover under a quarter of the video, so a
  threshold would lift the cap from nearly every Hooktheory source, not the thin ones.
- **Rejected: no rule, the judge weighs it.** Rule 1 exists to stop the judge reasoning its way to
  HIGH past a disagreeing human source (six HIGH verdicts overruled one); handing the decision
  back brings the drift back.
