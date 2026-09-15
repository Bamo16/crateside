# 0020 — the sidecar is parseable, and a warp marker is a second and a beat

- Status: Accepted
- Date: 2026-09-14
- Fulfils: [ADR 0002](0002-there-is-no-warped-signal.md), whose consequence "warp markers remain
  valuable and unbuilt … that is a future capability and needs its own ADR, not a quiet addition"
  this is the ADR for

## Context

ADR 0002 deleted the `.asd` *existence* check and was right to: the sidecar fires on a single
click to preview a file, so its presence cannot mean warped. It left the contents explicitly
open — warp markers would be **ground-truth tempo**, the user's own warp rather than a detector's
guess, and better than both the online BPM and `beatdetect`.

It also recorded what was then believed about the format: "~400 KB of Ableton's undocumented
binary serialization … with the bulk almost certainly waveform overview. There is no published
spec, so this would be reverse-engineering."

That guess was correct and is now **measured**. The file is not undocumented so much as
**self-documenting**: it carries a type schema of its own classes ahead of the data, so the
layout is read out of the file rather than assumed.

The occasion was ordinary use, not a planned build — a sample with several tempo changes, where
the question "what did I actually warp this to" had no answer outside Ableton's own UI.

## Decision

**crateside reads the warp section of the `.asd`, and reads nothing else in it.**

`AbletonWarpReader.TryRead` yields an `AbletonWarp`: `IsWarped`, the warp mode, the time
signature, and the ordered `WarpMarker` list. It is read-only, and it takes the layout from the
schema the file carries rather than from constants, so a Live version that adds a field moves the
offsets without breaking the reader.

**A `WarpMarker` is a `(Seconds, Beat)` pair and nothing else.** The sidecar stores no tempo
anywhere; a tempo is the slope between two consecutive markers, which is why `TempoAt` is a
method on the result and not a field in it.

**What a single BPM for a track should be is deliberately not decided here.** A marker list is
richer than one number and the reduction is lossy in a way that depends on what asks — this ADR
adds the source, not a policy for consuming it.

## What is measured, and on what

Every figure below is over the **652 sidecars under `Mashup Samples` on this machine on
2026-09-14**, not over the format in general.

- **The schema is the file's own.** `00 0a "SampleData"`, an `i32` type count, then that many type
  descriptors. All 652 declare an **identical 36-field `SampleData`**, and the fixed-width prefix
  before `WarpMarkers` is **124 bytes** in all of them.
- **A marker is 32 bytes**: a 12-byte class tag, an `i32` id, then `SecTime` and `BeatTime` as
  little-endian `f64`.
- **The marker list is zero-terminated, never count-prefixed** — 652/652. This was worth
  establishing because the `i32` immediately before the list looks exactly like a count and is
  not one: it is an **id allocator**, greater than every marker id in all 149 files sampled for it
  and equal to the highest plus one in 138.
- **Nothing in the file is a byte offset into it.** No `u32` anywhere equals the file length, the
  schema offset, or any section size. The leading table is **sample positions**, ending at the
  audio's total sample count.
- **The bulk is overview, as ADR 0002 guessed.** Median file 679 KB: ~6% leading sample-position
  table, ~93% trailing overview levels. The markers are a **median of 64 bytes** — two markers —
  and at most 19 KB.
- **Marker counts** run 2 to 598, median 2. Two files have none at all.

## What is not measured

- **The `WarpMode` names are convention, not observation.** Only values `4` and `6` occur here
  (649 and 1). The enum follows Live's conventional ordering and **has not been checked against
  the UI**; a value other than those two has never been seen and could be named wrong.
- **Type codes past the four fixed-width ones are unparsed.** The codes on the members after
  `WarpMarkers` — onsets, the overview — are skipped rather than understood, which is sound only
  because nothing before the marker list depends on them.
- **`IsWarped` has never been observed false** (650/650 true), so the reader's handling of an
  unwarped sample is untested against a real one.

## Alternatives considered

**Fixed offsets from a magic constant.** The 124-byte prefix is identical across the whole
library, so a constant would work today. Rejected: it would be a number nobody could re-derive
from the file, and the file supplies the real one for about forty lines of parsing.

**Anchor on the `WarpMarker` tag rather than the schema.** Simpler, and it was the first thing
built. Rejected on measurement: **two files have an empty marker list**, where there is no tag to
find and a tag search reports the schema's own declaration instead. The schema-derived offset
lands correctly on all 652.

**Parse the whole sidecar.** Rejected: the 93% that is overview data answers nothing crateside
asks, and every byte parsed is a byte that can break on a Live update.

## Consequences

- **ADR 0002's deletion stands.** Nothing here revives sidecar *presence* as a signal, and
  [ADR 0007](0007-the-move-out-of-staging-is-the-warp-signal.md) remains how a sample is known to
  be warped. This is a read of contents, which ADR 0002 said was the honest version of the idea.
- **A marker list is evidence of real warping in a way presence is not** — 2 markers is Ableton's
  automatic grid, while 598 is unmistakably a human. That distinction is available now and is
  **not yet used anywhere**; using it would be its own decision.
- keytag's `CONTEXT.md` lists `.asd` as a possible future BPM source. It is now a possible
  *present* one, though nothing consumes it yet.
- **The format is reverse-engineered and Ableton owes it no stability.** Every field is read
  through `Try`-shaped code that fails to `false` rather than throwing, and the real-library test
  parses all 652 sidecars — so a Live update that changes the layout shows up as a failing test
  rather than as wrong tempos.
