# 0028 — a file that was never cut carries its type in its comment

- Status: Accepted
- Date: 2026-09-24
- Settles the design of [#42](https://github.com/Bamo16/crateside/issues/42). Extends ADR 0022's
  permission to write a `COMMENT` tag.

## Context

A stem's **type** comes from its preset: crateside asked for `Vocal - Full`, so it knows. A file
that was never cut has no preset, and `TaggingRunner.TypeFor` maps that to `Instrumental`. That is
right for a **kept source** and wrong for an **added source** that is an a cappella. *Lil Wayne -
A Milli (A Cappella)* was tagged Instrumental on 2026-09-18 and corrected by hand.

Fixing only the add form and the tagging stage would not hold. A file arrives in **Mashup
Samples** with no tag-store entry (ADR 0023), and backing writes that entry from the file's own
provenance. For a file with no preset, that means `Type|Instrumental`. The write then reconciles
the index down to the store, so a correct `Type|Vocal` set in staging is overwritten on arrival.
So whatever records the type has to be readable by backing, from the file, for as long as the file
exists.

## Decision

**The user chooses Vocal or Instrumental when adding a source or adopting a sample, and crateside
appends it to the file's provenance comment as `| type: Vocal`.**

- For an **added source**, the type is appended to the `stemforge/` comment at handoff, the same
  kind of completion as a #45 claim. For an **adopted sample**, it goes into the `crateside/`
  comment adoption already writes.
- **A file with no preset and no `type:` stays Instrumental.** That is every kept source, and it is
  what the user wants: a kept source is used as an instrumental, and belongs under that filter.
- **Type means the part a file plays in a mashup, not what the audio is** (`CONTEXT.md`). So there
  is no third value: a full mix kept or added is being used as an instrumental.

## Considered options

- **The plan (`queue.json`).** No file writes, but backing would have to join library files to
  plans, and a file stays in the library long after its plan stops mattering.
- **Carrying the index's `Type` into the new entry during backing.** This would also protect hand
  edits, but it only works once the index has caught up with the move. A backing pass before that
  writes the entry without the type and destroys it.
- **A separate tag now.** StemForge's provenance is expected to move from one comment to separate
  tags. Its field names are StemForge's decision, and a crateside-only field would be a third
  format to read. The type moves with the rest when that happens.

## Consequences

- crateside now writes an audio tag on **every** added source, unasked, where ADR 0022 wrote one
  only when the user adopted something. The write touches metadata only, and the #45 hand claim
  measured the `.asd` sidecar staying byte-identical through the same kind of rewrite.
- The two added sources that predate this, `C_DIhh3sWO0` and `x3U22KHsrqE` (both `A Cappella`),
  need `type: Vocal` written in once.
- A `stemforge-cli` parameter that writes the type during the download would remove crateside's
  write. Not built. StemForge is where that change belongs.
