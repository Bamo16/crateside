# 0007 — the move out of `Sample Staging` is the warp signal, per sample

- Status: Accepted
- Date: 2026-09-02
- Qualifies: [ADR 0002](0002-there-is-no-warped-signal.md), whose reasoning stands and whose
  consequence "the board has no warped column at all" does not

## Context

The unified Tracks page specifies a per-stem **warped** column in the expanded row. ADR 0002
deleted exactly that column, and for a good reason: it had been driven by the presence of an
`.asd` sidecar, which Ableton writes merely for having *looked* at a file — a single click to
preview it is enough. That signal marks unfinished work as finished, which is the one direction
of error a worklist cannot tolerate.

So the column was specified and the only known source for it was one this repo had deliberately
removed. Asked directly, the user supplied the signal ADR 0002 concluded did not exist:

> the actual signal is when a sample has been moved from the Sample Staging folder to the Mashup
> Samples folder, which is invoked by the user in Ableton and results in Ableton moving the file
> on disk.

This is not new information so much as newly *applied*: ADR 0002 already says "the human work is
done when the stems leave `Sample Staging` for `Mashup Samples`", and `CONTEXT.md` says
"presence in Mashup Samples is what says a song is imported". What neither did was read that
per file and render it.

## Decision

**A sample is warped when it is in `Mashup Samples`, and that reading is per sample.**

Per sample rather than per track because **staging is emptied progressively** — ADR 0005 already
records this in the janitor's deletion predicate, which waits for *nothing* carrying the video ID
to remain in staging rather than for the song to be finished in one act. A user can warp and move
a vocal while its instrumental is still waiting, and a track in that state is still work in front
of them. A track-level reading would have to call that either done or untouched, and both are
wrong.

`ProcessedTracks` already opened every file under both roots and parsed its provenance; it kept
only the path. It now keeps the **root** it was found under and the **preset**. The root is the
signal. The preset is how a staged sample is matched to its finished copy, and it is the only
join that survives — **renaming is one of the human-limited steps**, so filenames do not.

**Nothing is marked, and nothing is stored.** The move is the user's own action, it covers
warping and tagging and renaming together, and it is self-cleaning in the same way the folder
layout already was. This keeps the reading derived, which is the property ADR 0002 was protecting
when it refused a hand-marked "warped" flag as "the thing most likely to drift, since the moment
of finishing is exactly when someone does not want to go and click something".

## Why this does not reopen ADR 0002

ADR 0002's argument is about the **`.asd` sidecar**, and every word of it stands: `.asd` means
Ableton looked at the file, it cannot distinguish abandoned work from finished work, and a
signal that cannot make that distinction is worse than no signal. Nothing here reads one.

What changes is the consequence, not the reasoning. 0002 concluded there was no *available*
signal and therefore no column; there is one, it comes from the file's whereabouts rather than
from Ableton's analysis, and it happens to be the same fact 0002 itself named as the definition
of done.

## Corroboration — measured 2026-09-02

Unplanned, and worth recording because it exercised exactly this path on live data. The user
finished two tracks that afternoon: `Eminem - Medicine Ball` (vocal and instrumental) and
`Goreshit - Toromi Hearts 2` (its source track, an instrumental release), dragging each into
`Mashup Samples` in Ableton's browser and deleting the drums.

Over the same evening, the Ableton tag store disagreed with reality in three separate ways —
entries retained for departed files, no entries at all for the arrivals, and neither store
written by the move or by a full quit and restart (recorded in
[ADR 0003](0003-crateside-may-write-abletons-tag-store.md)). **The files' whereabouts were
correct every single time.** A reading built on locations would have been right throughout the
period in which a reading built on tags would have been wrong three ways.

That is corroboration, not proof: one evening, one user, one workflow. It is recorded because it
is the strongest evidence available and because it arrived by accident rather than by looking for
a confirmation.

## Consequences

- **`Done` means every sample has reached `Mashup Samples`**, and a track with one sample still
  in staging remains the user's turn.
- **A sample already moved is not waiting to be tagged.** crateside never writes tags in
  `Mashup Samples`; the move covers tagging along with everything else, so a moved sample must
  not hold its track back in *tagging* for want of a tag crateside was never going to apply.
- **Warp guides are outside this entirely.** They are deleted rather than moved, so they never
  become warped and never make a track done. Their presence still says warping is outstanding.
- **A file deleted rather than moved reads as never finished**, because it is in neither root.
  That is the safe direction — it leaves work on the list rather than taking it off — and it is
  the same trade ADR 0002 chose.
- **`StemsFor` returns `StemOnDisk` rather than paths.** Every caller asked it for a count.
