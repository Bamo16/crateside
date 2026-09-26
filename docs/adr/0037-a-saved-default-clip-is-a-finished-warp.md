# 0037 — a saved default clip is a finished warp

- Status: Accepted
- Date: 2026-09-26
- Supersedes the decision of [ADR 0002](0002-there-is-no-warped-signal.md). Settles
  [#67](https://github.com/Bamo16/crateside/issues/67).

## Context

ADR 0002 gave the board no warped state, because the only signal then known, the sidecar's
existence, fires on a single preview click. It rejected a user-set flag as the one non-derived state
in the system and set parsing the sidecar aside on cost. ADR 0020 later made the sidecar parseable.

What was missing was a signal that tells a finished warp from one in progress. The user named it:
the **default clip** is saved only once the warp is good, as the last step before the move
(`CONTEXT.md`, Import). Two measurements on 2026-09-26 show the sidecar records exactly that:

- **Ableton writes nothing about a warp until *Save Default Clip*** (measured with the user,
  [note](../notes/2026-09-26-what-the-sidecar-records.md)). Pressing Warp and a full warp left the
  sidecar byte-identical; the save wrote the markers and the clip.
- **The sidecar carries a flag for it** ([note](../notes/2026-09-26-the-sidecar-field-by-field.md)):
  the byte ending the head is 0 before the save and 1 after. It agrees with the presence of warp
  markers on all 847 sidecars in the library and staging.

## Decision

**A staged sample whose sidecar holds a saved default clip is warped, and a track whose staged
samples are all warped is *ready to move*.**

- `AbletonWarpReader` reads the flag, walking the head's table to reach it and refusing the file if
  that walk does not land on the clip. It also reads the clip's start and end in beats, allowing for
  the Loop switch, because the start is where verse 1 begins and later work will want it.
- **Ready to move** is a state after *ready to warp*, the user's turn like it (amber), and in the
  *In staging* view. Its next step says to move the files, and to delete the warp guide first if one
  is still there. The Warp step in Track details shows done.
- A track part way through reads *ready to warp* and says how many of its staged samples are
  warped.
- Tagging still comes first: a sample with a saved clip but no key is still waiting to be tagged.
- **The move is still the only thing that says a track is done.** A saved clip moves nothing and
  writes nothing.

## Consequences

- **A warp in progress is still invisible.** Nothing on disk tells "not started" from "half done";
  both read *ready to warp*. This is ADR 0002's concern, and the new state never claims more than
  the save says.
- **A saved clip is only as good as the user's habit.** The save comes after the warp is good
  because that is how the user works, not because Ableton enforces it. A clip saved early would read
  as warped, and nothing on disk could show it. The one slip found so far is the opposite:
  *Kreayshawn – Gucci Gucci (Acapella)*, moved with no clip saved.
- **Staging's sidecars are read on every rebuild of the tracks view,** about 500 KB each. Staging
  holds a few songs at a time, so this was not measured as a cost.
- The board stays purely derived: the signal is read off Ableton's own file, which is what ADR 0002
  asked of any signal.
