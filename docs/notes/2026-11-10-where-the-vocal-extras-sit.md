# Where the vocal stems' extra transients sit

*Note, 2026-11-10, frozen. For [#153](https://github.com/Bamo16/crateside/issues/153); follows
[the 2026-10-02 note](2026-10-02-transients-against-lives.md), whose scoring it reuses (a match is
within 25 ms, nearest unused, against Live's own list).*

## Question

That note found vocal stems the outlier: precision 52%, 1.40 of crateside's transients for each of
Live's. Is that silence between phrases, which an absolute floor would remove?

## Measured

On the library's sidecars whose `OriginalFileSize` and length still match the file (16 vocal,
10 instrumental and 1 drum stem, taken evenly from the sorted list), one detector pass each, then
dropping every transient whose attack envelope never reaches the floor. The envelope is the detector's
own, the mean absolute amplitude per 32 samples; the floor is dBFS.

| floor | vocal recall | vocal precision | vocal count against Live's | instrumental recall | instrumental precision |
|---|---|---|---|---|---|
| none | 71.5% | 48.0% | 1.49 | 89.3% | 78.0% |
| −60 | 71.5% | 48.7% | 1.47 | 89.3% | 78.1% |
| −50 | 71.4% | 49.8% | 1.43 | 89.3% | 78.2% |
| −45 | 71.0% | 50.7% | 1.40 | 89.3% | 78.3% |
| −40 | 70.2% | 51.6% | 1.36 | 88.9% | 78.4% |

(The one drum stem: recall 97.4% with no floor, 97.3% at −50; precision 84.9% to 86.3%.)

**The vocal extras are not mostly silence.** Even at −40 dBFS, loud enough to cost recall, the count
against Live's falls from 1.49 to 1.36. They sit in audible passages, so a floor does not close the
precision gap; that stays the placement problem of the earlier note.

## What the floor is for

A stem with nothing in it still got a full list: the peak picker is relative to the file, so the
loudest bleed counts as a peak. The floor is absolute. **−50 dBFS** (the level `WarpTempo` already
counts as silence) costs 0.1 point of vocal recall, nothing on instrumentals, and `OnsetDetector`
now marks nothing in a stem that never gets louder, as Live does.

**Not measured:** the stem in the issue (Adele – *Someone Like You*, a drumless drum stem) is no
longer on disk, and no sidecar in the library or Staging holds an empty transient list from Live, so
the level of its bleed is unknown. The floor is tested against synthetic bleed at −60 dBFS only.
