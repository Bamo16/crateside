# 0014 — the song is the identity, and the name is not

- Status: Accepted
- Date: 2026-09-10
- Finishes [ADR 0012](0012-the-source-url-is-an-instruction-not-an-identity.md), which decided
  the source URL is an instruction and left *"what does the row show?"* open.
- Rests on [keytag ADR 0008](../../../keytag/docs/adr/0008-a-restatement-is-not-a-run.md), which
  froze `song` across a restatement.

## Context

`Ludacris, Pharrell - Southern Hospitality` was swapped on 2026-09-09. The pipeline did its job
and the state it left behind was one song wearing two of everything.

## What was measured, 2026-09-10

From `jobs.jsonl`, `runs.jsonl` and the stems' own provenance:

```
00:22:02  AhtkGlO7O8c:Separation  Canceled  "source swapped"
00:22:02  AhtkGlO7O8c:Handoff     Canceled  "source swapped"
00:22:02  AhtkGlO7O8c:Tagging     Canceled  "source swapped"
00:29:45  AhtkGlO7O8c:Separation  Running
00:37:49  AhtkGlO7O8c:Separation  Succeeded
00:35-39  39Z_YiOmHeA:Handoff     Succeeded
02:43:36  39Z_YiOmHeA:Tagging     Succeeded
```

```
Sample Staging   (Drums)2, (Instrumental - Full)2, (Vocal - Full)   →  39Z_YiOmHeA
Mashup Samples   (Instrumental - Full), (Vocal - Full)              →  AhtkGlO7O8c
```

1. **Separation runs under the old ID; handoff and tagging complete under the new one.**
   Separation is driven by the queue item, which by ADR 0012 keeps the original video ID.
   Handoff and tagging are driven by the stems, and StemForge stamps those with the video it
   actually downloaded.

2. **So `AhtkGlO7O8c:Handoff` and `:Tagging` stay `Canceled` forever.** `JobStore.All()`
   projects the current state per `(videoId, stage)`, and `ReadReopenedTracks` treats
   `Canceled` as reopened — so the old ID sits in `AwaitingSeparation` permanently. That is the
   `judged · in line` row and the header's `1 waiting` that never clears.

3. **It will not re-separate**, which was worth checking before calling it urgent.
   `SeparationSelector` passes `retrySucceeded: false` and that ID's separation succeeded. The
   cost is a permanently wrong header and a phantom row, not GPU.

4. **The ledger already resolves both IDs to one song.** Both lines carry
   `song: "ludacris, pharrell - southern hospitality"`, and `KeyLedger._songByVideoId` maps
   every video ID it has ever seen onto it. `TryGetLatestByVideoId` returns the same run for
   both, today, with no code change.

## Decision

**A song's identity is keytag's `song` slug. The video ID is the key for *work*, not for
identity.**

Three things were being carried by one field and are now named separately:

| | what it is | who owns it | may change |
|---|---|---|---|
| **Identity** | keytag's `song` slug | keytag, fixed at the first run | never |
| **Name** | what the user sees, and what the files are called | crateside, via `TrackInstruction.Name` | any time |
| **Source URL** | which upload to fetch | crateside, an instruction (ADR 0012) | per swap |

**Rows are keyed by song where there is one, and by video ID where there is not.** A track with
no verdict has no song yet, and needs none: a swap can only follow a verdict, because not
re-judging is the entire reason a swap exists. The identity is therefore always available at the
one moment two video IDs must merge.

**Job state is projected per `(song, stage)`, not per `(videoId, stage)`.** The latest record of
a stage for a song is what that stage's state is, whichever of its video IDs carries it. The
phantom above then stops being true rather than being patched: the latest `Handoff` for this
song is `39Z_YiOmHeA` `Succeeded`, which is later than `AhtkGlO7O8c` `Canceled`.

### Why not the first video ID as a canonical key

It was the obvious alternative and it builds a **second** linkage beside the one keytag already
publishes. Two maps of one relationship that have to agree is the failure this repo has been
bitten by twice in a week — a repair script and a check each walking their own copy of a field
list while 97 damaged strings sat between them, and `QueueService.Reopened` copying instruction
fields by hand while `QueueView.Compose` merged them properly. A first-ID map would be a third,
and it would need persisting, where the song identity needs nothing stored at all.

### Why identity is not the name

They share a source — `song` is `lower("{artist} - {title}")` from the same yt-dlp fields
StemForge names files with, and on this track they agree exactly. That is what makes tying them
tempting.

Identity has to be immutable and the name has to be correctable, and the user renames tracks
routinely. If identity were the name, a rename would re-file every stem, break the provenance
lookup and re-judge the track.

### The risk this accepts

Two genuinely different songs whose artist and title slug identically would merge. keytag
already uses `song` as its supersede key, so such a collision would already mis-supersede
verdicts — crateside inherits an accepted invariant rather than creating one.

## Consequences

**One row per song, showing every sample across every video ID it has.** Which is what
[#13](https://github.com/Bamo16/crateside/issues/13) asked for, and each sample carries the
video ID it was cut from, since the filenames cannot always tell them apart.

**A row's state is the union of its video IDs' work.** The state and filter vocabulary is
untouched — that is [#10](https://github.com/Bamo16/crateside/issues/10) and
[#11](https://github.com/Bamo16/crateside/issues/11)'s business, and merging rows is not license
to redefine what they mean.

**A swap can rename the stems, and that is new.** StemForge names from the metadata of whichever
video it downloads, so an upload without an `artist` field falls back to the bare video title and
the replacement lands under a different filename than the thing it replaces. Here the two
uploads carried identical YouTube Music metadata and matched by luck. The song's established
name wins, applied through the rename flow crateside already has.

**The predicted filename is knowable before any work runs.**
`stemforge-cli download --list-formats --json` resolves yt-dlp metadata without downloading and
already reports `Artist` and `Title` — no new flag. crateside shows YouTube's video title today,
which is not what the files will be called, so the user cannot tell whether a rename is needed
until after the work is done.

**Deleting a superseded version stays split by root.** Staging is crateside's to manage, so
superseded samples there may be removed once the replacements land. `Mashup Samples` is the
user's and ADR 0003 forbids crateside writing there at all.

## Alternatives considered

**Group finished rows by song after composing them.** Cheaper to write and it reverse-engineers
from finished rows the same choices that are better made once, explicitly, while the per-video-ID
facts are still in hand.

**Have crateside give StemForge the name to use.** Would make the filename follow identity at the
source rather than being corrected afterwards. Rejected for now because it needs a StemForge CLI
change where the rename flow already exists here, and because the user's own rename has to be
applied after the fact regardless.
