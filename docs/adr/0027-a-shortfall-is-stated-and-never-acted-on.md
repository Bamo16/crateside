# 0027 — a shortfall is stated and never acted on

- Status: Accepted
- Date: 2026-09-23
- Settles the design questions in [#38](https://github.com/Bamo16/crateside/issues/38),
  [#33](https://github.com/Bamo16/crateside/issues/33) and
  [#32](https://github.com/Bamo16/crateside/issues/32), and the open work-list question in
  [#49](https://github.com/Bamo16/crateside/issues/49).
- Names two terms the glossary had been leaning on without defining: **shortfall** and **re-cut**.

## Context

Three bugs filed separately turned out to be one shape. Every affordance on the Tracks page is built
on *changing the plan*, and each of the three is a gesture that is not that:

| issue | the gesture that has no expression |
|---|---|
| #38 | make what the plan already asks for and disk lacks — **the plan does not change** |
| #33 | stop wanting this track at all — not "reduce the plan to nothing", which the empty-plan guard refuses |
| #32 | want something different while a cut is already running |

They shut each other's exits. #33's track cannot be removed because its plan regenerates the sample
that blocks removal; #38's track cannot be re-cut because the only gesture that opens a confirm is
*changing* the plan, and the plan is already right. Both were reached twice: #38 by vanished files on
2026-09-19 and again by a killed separation on 2026-09-23 (ADR 0025), where the user went looking for
the affordance and reported its absence.

Two things were measured while designing, and both changed the answer.

**The domain layer already does what #38 needs.** `StemRecut.Apply` on an unchanged plan has an empty
`Removing`, rewrites the same plan harmlessly, and `MakesSomething` is true because the recipe is
narrowed by `StemInventory.Outstanding` — so it reopens the stages and the track re-queues.
`RefusalFor` returns null. Only the UI's `_plan = wanted.AsksTheSameAs(row.Plan) ? null : …` stood in
the way. #38's own claim that "only the affordance is missing" was right.

**#32's picker gate is a symptom, not the bug.** A plan change made during a separation is *erased*:
`StemRecut.Apply` records `Separation → Cancelled` to reopen it, the running separation then finishes
and `interlock.Release` records `Separation → Succeeded`, and the latest record wins. Opening the
picker alone would have produced a gesture that appears to work and silently does nothing.

## Decision

**A shortfall is a statement, not an instruction.** Crateside says a track is short and offers the
gesture; it never makes up a shortfall on its own.

The undecidable case is the reason. A stem the user deleted on purpose and a stem that was never made
are indistinguishable on disk, and `SeparationSelector` already says so in refusing to retry a
success: *"a recorded success with no stems on disk most likely means they were deleted on purpose."*
Acting on a shortfall automatically would re-cut a file the user had deliberately removed, for ever,
against them — and it would spend the GPU to do it, which is theirs to spend.

So:

- **A shortfall affects what the row says and nothing else.** `TrackRow.State` stops reading as
  finished while a track is short, and the row announces the shortfall and carries the gesture.
  `ProcessedTracks.FullyProcessed`, `QueueView.Outstanding` and `AwaitingSeparation` are untouched.
- **The gesture lives on the row, not in the picker.** Discoverability is the failure that actually
  bit: with HARDCODED the user opened the picker, saw the line ticked, and nothing said the file was
  absent. Learning a file is gone must not require fishing in the picker.
- **Remove means "I do not want this track".** It drops the instruction *first*, so nothing
  regenerates, then recycles what crateside put in `Sample Staging` and the working set, and reports
  the count the way the handoff already reports superseded files it cleared. Anything that reached
  `Mashup Samples` is the user's and is never touched (ADR 0014). `CanForget` therefore stops testing
  `Samples.Count` at all.
- **A plan change during a cut is recorded immediately and makes nothing.** The picker opens, the
  running cut finishes with the recipe it was given, and the shortfall then carries the gesture. No
  pending-change machinery and no mid-run reopening, so there is nothing for the finishing run to
  erase. The *destructive* half stays refused while a cut is in flight, because the separator may be
  writing the file being dropped.

**"A track has to keep something" stays.** It protects a plan being edited, and Remove is now the way
to discard — so the guard no longer traps anything.

## Consequences

- The three issues become one small change each, sharing one concept. #38 is the row saying it plus
  the `_plan` condition; #33 is an ordering change in Remove; #32 is a gate change plus refusing the
  destructive half.
- **`StemRecut` will do more than its name says.** Under this ADR a re-cut is cutting a shortfall,
  while `StemRecut` also writes plans and deletes dropped files. Flagged rather than renamed —
  splitting it is a change for its own commit and nothing here needs it.
- `StemInventory.Outstanding` is renamed `Shortfall`, leaving `QueueView.Outstanding` as the only
  `Outstanding`. The two meant different things at different levels and shared a name.
- Nothing here closes #49's other half, the adopted separation that is never released, or its
  observation that `ProcessedTracks` counts *a* stem. The second is now deliberate rather than
  overlooked: fully-processed stays a disk fact, and the shortfall is the thing that knows better.
- A track can now sit visibly short for ever, and that is intended. The alternative was spending the
  user's GPU on a guess about why a file is absent.
