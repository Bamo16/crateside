# 0018 — the plan is the record, and the warp guide is not part of it

- Status: Accepted
- Date: 2026-09-12
- Answers [#22](https://github.com/Bamo16/crateside/issues/22).
- Reverses the recording decision in
  [ADR 0004](0004-sample-is-the-role-word-and-stem-is-the-origin-word.md); everything else that
  ADR settles is untouched.

## Context

ADR 0004 ruled that crateside records a **fact** about the release — the user says it has no
vocals — and derives the separation recipe from it. The reasoning was that a fact "still explains
itself when the workflow changes", where a stored plan would become a lie the moment the workflow
moved under it. That held while there was exactly one fact and exactly two recipes.

The stem picker ends that. The separator offers nine presets, the user wants to hold several sets
of stems at once, and the source track is wanted as a sample in its own right — not only for a
release with nothing to separate, but because crossfading an instrumental stem into the original
often beats layering two stems of the same track. Asked what fact sits underneath a track carrying
`instrumental_clean`, `instrumental_full` and the source, the honest answer is: none. There is only
what the user asked for.

## Decision

**The plan is the thing recorded.** `StemPlan` — a list of preset ids plus whether the source is
kept — is stored on `TrackInstruction` and nothing derives it. `SeparationRecipe.From(plan)`
replaces `SeparationRecipe.For(fact)`.

The old fact converts on read rather than by migration: an instruction carrying
`instrumentalRelease: true` yields `StemPlan.InstrumentalRelease`, and writing the file back drops
the field. Eleven tracks in `queue.json` are in that state.

**An instrumental release is not a mode.** It is *the vocal preset unticked and the source ticked*,
which is the same statement in the picker's own vocabulary and needs no special case anywhere.
`StemPlan.InstrumentalRelease` exists as the conversion target and as a name for that shape, not as
something the picker offers.

**`has_vocals` is deprecated outright, and nothing infers a replacement.** It could have been kept
alive by inferring `false` when no vocal preset is ticked and the source is kept, labeled in the
ledger as an inference — but there is nothing to keep alive. The field is vestigial: keytag once
ran the whole workflow and needed telling which stems to ask for, and
[ADR 0001](0001-crateside-orchestrates-the-workflow.md) moved that to crateside long ago. The
verdict prompt's own line said so —

```
- has_vocals: false — the user states this is an instrumental release.
  Record it; do not re-derive it.
```

— and keytag's `SKILL.md` says it twice more. It is a pass-through to the ledger and nothing reads
it, so removing it costs no judging signal. Measured 2026-09-12: 259 ledger rows, 130 carrying the
field, 11 `false`, 119 `null`, 0 `true` — the same eleven tracks as `queue.json`.

**The warp guide is not part of the plan.** `SeparationRecipe.From` sets `ExtractDrums` regardless
of what the user ticked. A drum stem is not a sample: it is made to be looked at, used for placing
warp markers, and deleted afterwards (ADR 0004), and no workflow exists that does not want one. In
the picker it is a pinned line with a dot rather than a tick. Should drums become a preset upstream,
the line stays pinned — the reason is retention, not how the file is produced.

## Considered and rejected

- **Keep the fact and derive as before, with the picker as an override.** Rejected: two records of
  the same thing, and the first one to be edited makes the other a lie. The failure ADR 0004 was
  guarding against arrives anyway, by a different door.
- **Infer `has_vocals` from the plan and label it an inference.** Rejected: nothing consumes it, so
  the only effect is a nullable field every future reader has to be told to ignore.
- **Offer an "instrumental release" shortcut in the picker.** Rejected: it reaches
  today's behavior with no special mode, and keeping the control simple is worth more.

## Consequences

**What the user asked for and what exists on disk are now in different vocabularies.** The plan
says `vocal_full`; the file says `Vocal - Full`. While the recipe was derived from a fact this
never came up, because nothing ever had to ask *which of these have already been made*. An additive
re-cut does, and the rule that relates the two belongs to StemForge —
`presets --json` must report the display name per preset before that join can be written. Blocks
the additive half of #22 and the picker's inventory alike.

**`TrackRow.IsInstrumentalRelease` is interim and marked for deletion.** It exists only to keep the
Release select working until the picker replaces it. A plan is no longer a fact about the release,
so nothing should be asking this question.

**keytag's half is outstanding.** `ledger.py` (schema, the written row, and `RESTATED_FIELDS` —
restatements copy the field, so [keytag ADR 0008](../../../keytag/docs/adr/0008-a-restatement-is-not-a-run.md)
is in scope), `detect-key/SKILL.md`, `record_result.py`, `results/README.md`, `CONTEXT.md` and two
ledger tests. Then a one-time pass over all 259 rows, so no reader ever has to know the field
existed. Until that runs the ledger still carries it and crateside simply stops writing it.

**The eleven converted tracks must keep their plan.** They express "instrumental" today as
`instrumentalRelease: true`, which *derives* the recipe. Under this decision nothing derives it, so
a re-cut with no stored plan would silently default them back to vocal + instrumental. The
conversion on read is what prevents that, and it is the one migration step in this change.
