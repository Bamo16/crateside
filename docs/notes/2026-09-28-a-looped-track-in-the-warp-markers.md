# A looped track in the warp markers

*Note, 2026-09-28, frozen. For [#68](https://github.com/Bamo16/crateside/issues/68). Measured from
the warp's own markers; no audio analyzed.*

The user's hypothesis about *Cypress Hill – Hits from the Bong* (owned): the backing is a drum loop a
human played, repeated through the song, with hits far enough off the grid that the warp needed
markers to correct them.

**Measured.** 168 markers; a least-squares line through them all is 92.145 BPM and leaves deviations
of up to 28 ms (sd 12 ms). The share of that deviation explained by a marker's position within an
L-bar loop, against 1000 random shuffles of the positions:

| loop | explained | shuffled, 99th percentile |
|---|---|---|
| 1 bar | 53 % | 6 % |
| 2 bars | 79 % | 10 % |
| 4 bars | 79 % | 15 % |
| 8 bars | 80 % | 24 % |

Averaged over each third of the song the markers sit at +1.3, −1.6 and +0.3 ms from the line, so the
tempo does not wander. Within the 2-bar loop, beat 2 of each bar is +12 ms and bar 2's downbeat
−19 ms, repeating with a spread of 2–4 ms on beats 2 and 4 and 9 ms on the downbeats. This is why
every 30 s window of `beatdetect` reads 92.15 ([mixes note](2026-09-28-beatdetect-mixes-pooled-parts-and-unsteady-tempo.md)).

*I Want You Back* (136 markers) shows no loop pattern at any length (explained ≤ 16 %, below its
shuffled median at 2 bars and up), but wanders: +13.3, −22.6 and +9.1 ms across the thirds, sd 34 ms.

The repeat spread also bounds the user's marker error on this track: the same snare, marked 28–29
times, varies by 2–4 ms. One track; the user's own estimate of 5–10 ms stands.

Terms agreed with the user the same day: a **drifting** track is written at one tempo and performed
without a click; **wandering** and **looped** are its two kinds so far; a **tempo change** is
intended. They are not yet in `CONTEXT.md`.
