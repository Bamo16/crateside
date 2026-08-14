# crateside

A helper bench for the parts of the Ableton mashup workflow that only a human can
do — **warping, tagging, renaming** — plus the intake queue that feeds them.

A Blazor Server app, run locally, left open on a second monitor while working in
Ableton. Three views:

- **Staging board** — every track in `Sample Staging` with its key and BPM, and how
  far through warping and tagging each of its stems is. Replaces scrolling back
  through agent conversations to find the key/BPM table for the current batch.
- **Partners** — pick a key (or a track from the board) and get the compatible keys
  to filter for in Ableton's browser, ordered by how cheap they are to try.
- **Queue** — the YouTube intake playlist: what is in it, what is already processed,
  what is new. Removes processed items once they are genuinely done, and **drains the
  queue** by separating outstanding tracks one at a time.

## Relationship to keytag

`crateside` reads [`keytag`](../keytag) as **data** and keytag never learns it
exists — the same one-way boundary keytag drew around `keydetect-harness`. It
reads two things:

- `keytag/results/runs.jsonl` — the key/BPM ledger (schema in that repo's
  `results/README.md`)
- the Ableton folders on `P:\` — `Ableton Folder Info\*.xmp` for applied tags

There is deliberately **no warp signal**: a `.asd` sidecar means Ableton analysed the
file, not that it was warped, so it is not read at all ([ADR 0002](docs/adr/0002-there-is-no-warped-signal.md)).

**The Staging board holds no state of its own.** Everything on it is derived from
those sources, so nothing can fall out of sync and nothing is marked done by hand.
The app keeps exactly two things: the OAuth refresh token for playlist writes, and a
record of the work it has run ([ADR 0001](docs/adr/0001-crateside-orchestrates-the-workflow.md)).

The design and its rejected alternatives are recorded in
[`keytag/docs/adr/0003`](../keytag/docs/adr/0003-a-separate-blazor-app-owns-the-human-half.md).
Domain language is in [CONTEXT.md](CONTEXT.md).

## Status

v1 is complete: all three views work against the real library, and playlist removal has
been exercised. crateside is now taking over the workflow decisions that used to live in
keytag's `detect-key` skill — see [ADR 0001](docs/adr/0001-crateside-orchestrates-the-workflow.md).

## Working on it

```
dotnet watch --project src/Crateside run   # the usual way to work on the UI
dotnet test --solution Crateside.slnx      # MTP; a positional solution path is an error
dotnet csharpier format .                  # before every commit
```

Many tests assert against the real Ableton roots and the keytag ledger, and skip when
those are absent — that has repeatedly caught things fixtures would not have.
