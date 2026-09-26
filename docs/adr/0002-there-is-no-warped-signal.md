# 0002 — there is no reliable "warped" signal, so the board does not claim one

- Status: Accepted; the decision superseded by [ADR 0037](0037-a-saved-default-clip-is-a-finished-warp.md) on 2026-09-26
- Date: 2026-08-12 (recorded 2026-08-14, distilled from the first-build handoff)

## Context

The Staging board shows how far through the **human-limited steps** each stem is.
Those steps are warping, tagging and sometimes renaming, and the board was designed to
show two of them per stem: *tagged* and *warped*.

**Tagged** is genuinely observable — Ableton records applied Key keywords in the
per-root `Ableton Folder Info\<uuid>.xmp`, and crateside reads it.

**Warped** appeared to be equally observable. Ableton writes a `.asd` sidecar next to
files in the library, and the design assumed its presence meant the file had been
warped. A reader was built on that assumption.

**It is wrong, and it was corrected by the user on 2026-08-12 after the code existed.**
A `.asd` means only that Ableton has *analysed* the file. It was observed present on stems
where the user had opened the file, started to think about warping, and stopped because
they were not ready to do the work. So the sidecar fires on *intent to look*, not on *work
completed*.

**The trigger is weaker still than first recorded.** This ADR originally said analysis
happens "on dragging a clip into Arrangement view". While testing something unrelated on
2026-08-14, a `.asd` appeared for a file created minutes earlier that had never been
warped, opened in a project, or dragged anywhere — **a single click in the browser to
preview it was enough**, because Ableton analyses the file to render the waveform. The
signal is even further from "warped" than the correction assumed.

This matters more than an ordinary wrong reading, because the error is directionally
dangerous: it marks unfinished work as finished. The board exists to answer "what is
still in front of me", and a false "done" removes something from that answer.

## Decision

**The `.asd` existence check is deleted rather than downgraded, and the board has no
warped column at all.**

A stem's per-stem progress therefore reduces to whether it is **key-tagged**.
Completion is not a flag on the board: **the human work is done when the stems leave
`Sample Staging` for `Mashup Samples`**, so a finished song simply stops appearing.

Location as the completion signal has the same property the XMP has: nothing is marked
done, things simply move. Both are self-cleaning, which is what lets the board stay
**purely derived** with no store of its own.

## Alternatives considered

**Keep `.asd` as a weaker "started" indicator.** Tempting, since it does mean *something*
happened. Rejected: the board's value is that everything on it is trustworthy at a
glance, and a column meaning "possibly touched, possibly finished, possibly abandoned"
is worse than no column — it invites exactly the misreading that caused this. A signal
that cannot distinguish abandoned work from finished work is not a progress signal.

**Parse the `.asd` to get real warp data.** The honest version of the above, and it would
genuinely answer the question. Rejected for v1 on cost, not on value — see below.

**Have the user mark stems warped by hand.** Rejected: it would be the only non-derived
state in the system, and the thing most likely to drift, since the moment of finishing is
exactly when someone does not want to go and click something.

## Consequences

- The board reports **tagged** per stem and nothing else. Anything richer would need a
  signal that does not currently exist.
- **A song that is finished is a song that is absent.** The board is a worklist, not a
  history — history lives in the ledger.
- **Warp markers remain valuable and unbuilt.** If they can be extracted they are
  *ground-truth tempo* — the user's own warp, set by feel, rather than a detector's
  guess — which would beat both the online BPM and `beatdetect`. That is a future
  capability and needs its own ADR, not a quiet addition.
- **What is actually inside one** (inspected, not assumed): ~400 KB of Ableton's
  undocumented binary serialization — `SampleData`, then repeated `RemoteableDouble` /
  `RemoteableBool` / `UserFloat` / `RemoteableEnum` typed fields, with the bulk almost
  certainly waveform overview. There is no published spec, so this would be
  reverse-engineering.
- keytag's `CONTEXT.md` still lists `.asd` as a possible future BPM source. That remains
  correct and is not in tension with this: it is a future *read* of the file's contents,
  not a use of its *existence*.

## Postscript

This is the second decision in this repo to have rested on an unverified premise, after
ADR 0001's GPU-contention claim. Both were caught by asking the user rather than by
reasoning harder, and in both cases the code had already been written. The pattern worth
naming: **a fact about someone else's tools or habits is a fact to check with them, not
to infer** — inference produces something that looks settled and is merely plausible.
