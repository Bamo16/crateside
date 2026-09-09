# Swapping a source video

**Status:** a request to keytag, and a note on where the boundary falls. Not a decision, and
nothing here is built. Written 2026-09-09 after establishing what re-cut needed, which turned
out to be nothing (see the commit that let re-cut fetch its own audio).

## What is wanted

A track is in the library from a bad upload — wrong master, a radio edit, an eight-second
silence at the front — and the user finds a better video of **the same song**. They want
crateside to separate that one instead, keeping everything already established about the song:
its key, its BPM, the verdict that cost a Claude session to produce.

The song has not changed. Only which file it is cut from.

## Why it does not work today

StemForge writes the source URL into each stem's `COMMENT` provenance, and that is the only
thing tying a file on disk back to a verdict. Separate from a new video and the stems carry the
**new** video ID.

crateside resolves that ID through `KeyLedger`, in two hops:

```
_songByVideoId[videoId]  ->  song identity
_latestBySong[song]      ->  the current verdict
```

Both maps are built purely from keytag's `runs.jsonl`. A video ID that has never appeared in a
run is absent from the first map, so the lookup fails at hop one. The stems land with no
verdict, no key, and nothing to tag them with — and `VerdictSelector`, seeing
`ledger.HasRunFor(id)` false, would pick the track up and judge it again, spending a session to
re-derive a key already known.

So something has to say **this video ID is that song**. That is a statement about song identity,
which is keytag's concept and lives in keytag's file.

## The trap: an alias written carelessly deletes the verdict

The obvious move is to append a line carrying the existing `song` and the new `source_url`. It
does fix hop one. But `_latestBySong` takes the **latest line by timestamp** for a song, so a
newly appended line does not sit alongside the existing verdict — it *becomes* it.

A minimal alias line, carrying no `verdict` block, would therefore resolve the video ID
correctly and simultaneously blank the key for the whole song. Every stem of it, including the
ones already tagged and imported, would read as unjudged.

Measured rather than argued: `A_later_line_carrying_no_verdict_takes_the_songs_key_away` in
`KeyLedgerTests` writes exactly that pair of lines and asserts both halves — the new ID resolves,
and the key is gone through the **old** ID as well, so the damage is not confined to the swap.

**Whatever shape this takes, that is the constraint to design against:** append-only plus
latest-wins means there is no such thing as adding a fact about a song without also restating
its verdict, unless the schema grows a line that `_latestBySong` deliberately skips.

## Two shapes

### A — re-file the verdict under the new URL

keytag appends a complete run line: the existing `song`, `verdict`, `bpm` and `has_vocals`
copied verbatim, with the new `source_url` and a fresh `timestamp`.

- **crateside needs no change at all.** Both hops resolve, and the verdict is identical to the
  one it replaces, so superseding it is harmless.
- **It costs no schema change.**
- **But it writes a line claiming a run that never happened.** keytag ADR 0001 settled
  `runs.jsonl` as an activity log, one line per run; this puts a non-run in it. `machine` would
  be absent or a copy, and the recall and tuning uses the log was built for now have a
  duplicate to explain.

### B — a line kind that is not a run

keytag records the alias as its own thing — a `kind` discriminator, or a `source_urls` array on
the existing run, or a separate file. `_songByVideoId` reads it; `_latestBySong` ignores it.

- **Honest.** An alias is not a verdict and does not pretend to be one, and the log keeps
  meaning what it says.
- **Costs a schema change on keytag's side**, and a small one on crateside's: `KeyLedger` builds
  its two maps in one pass today, and would need the alias lines in the first and out of the
  second.
- Fits the direction [ledger-as-an-interface.md](ledger-as-an-interface.md) already argues for,
  whose whole theme is one field doing two jobs.

**Recommended: B**, unless the swap turns out to be rare enough that a hand-written line is
fine. A is a fast path that quietly makes the log less true, and the log's truthfulness is the
thing keytag ADR 0001 was protecting.

## Where the boundary falls

Worth stating plainly, because the question was open:

- **keytag owns "which song a video ID is."** It is song identity, it is the join every consumer
  depends on, and it belongs in the file that already carries it.
- **crateside owns "which URL to fetch."** That is a work-order input, not a fact about the
  song. It belongs beside the other standing instructions in `TrackInstruction`, next to `Name`,
  `IsHeld` and `IsInstrumentalRelease` — an override that `WorkOrder.Compose` prefers over the
  playlist entry's own watch URL.

Those two together are the whole feature. Neither repo needs to know how the other stores its
half.

## What crateside would do with it

Once the alias exists and the URL override is recorded, the existing machinery covers the rest —
this is the part that turned out to be already built:

1. Discard the track's working set, so the old audio is not reused.
2. Reopen separation, handoff and tagging, exactly as re-cut does.
3. `SeparationRunner` finds no retained audio and passes the source URL instead, which is now the
   new one. It already does this, including `--keep-source` and `--format` on the URL branch.

Evidence and verdict stay closed. No session is spent.

## Open questions

- **Does the old video ID keep resolving?** Under both shapes it does, since nothing removes the
  original run. That seems right — stems cut from the old video are still that song — but it has
  not been thought through against a track that gets swapped twice.
- **Should crateside ever propose a swap, or only accept one?** Everything here assumes the user
  found the better upload. Detecting a bad master automatically is a different and much larger
  problem.
- **What does the row show?** A track whose source has been swapped is not visibly different from
  one that has not, and the derived name may still come from files cut from the old video.
