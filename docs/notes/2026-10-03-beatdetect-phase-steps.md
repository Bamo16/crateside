# beatdetect's phase steps, against the library (2026-10-03)

FiLL built the per-window phase check suggested after Lil Jon & The East Side Boyz, Ying Yang Twins – *Get Low* (beatdetect branch)
`feat/phase-step-detection`, commit `62bdb97`, version 1.2.0 unreleased): `phase_stepN`,
`phase_bpm` and `phase_noise_ms`. Built locally for Windows and run on every Mashup Samples
instrumental whose saved warp is a single marker (388), and the 13 kept drum stems whose
instrumental has one.

References: the warp's tempo for `bpm` and `phase_bpm`; for steps, `GridCheck.Against` on the same
file's Live transients at the warp's tempo (stronger half, 10 s windows, the 16th grid, a step at
15 ms).

## Get Low

| file | step reported | GridCheck |
|---|---|---|
| drum stem | +52.8 ms at 4:40.5, and −17.2 ms at 2:05 | +48.0 ms at 4:40; at 2:05 a smooth drift, −3.0 to −3.9 ms |
| instrumental | −81.4 ms at 4:39.7 (noise 13.1 ms) | +50.2 ms at 4:40 |

The drum stem places the paste and sizes it within 5 ms. The 2:05 step is not in the audio. The
instrumental places it but reports the wrong size and sign.

## Across the library

| | instrumentals | drum stems |
|---|---|---|
| `bpm` within 0.01 of the warp | 361 of 388 (median 0.0021) | 12 of 13 |
| `phase_bpm` within 0.01 | 200 of 373 (median 0.0087) | 10 of 13 |
| `phase_bpm` closer than `bpm` | 75 of 373; worse by more than 0.01 on 139 | 4 of 13 |
| `phase_noise_ms` median (p90) | 5.9 (19.9) | 2.8 (25.1) |
| files with a reported step | 208 of 382 | 9 of 13 |
| files with a GridCheck step | 19 of 382 | 0 of 13 |
| steps both report (within 10 s) | 9, 8 of them the same sign | 0 |

The reported steps are mostly large: median 50 ms, p10 20 ms. 191 of 409 are about an eighth of a
beat, 47 a 16th and 26 half a beat. Three checked by hand (Will Smith, Dru Hill, Kool Mo Dee –
*Wild Wild West*, Birdy – *Wings (Remix)*, Sub Focus, Grimes – *Entwined*), reported at 69, −51 and
−52 ms, sit where GridCheck's windows stay within ±3 ms.

Neither noise nor octave explains it: files with a step are about half of every noise band, and
about half of both the octave-folded and the unfolded.

## What it points at

The phase is read on the beat from every frame's onset energy. When the arrangement changes (hats
or an eighth-note figure come in), the energy-weighted angle moves although nothing moved in time.
GridCheck folds onto the 16th, where every onset of a straight pattern lands at one phase, and
reads only the louder half of the transients. Its window noise on the same instrumentals is 2.3 ms
(#86), against `phase_noise_ms`'s 5.9.

`phase_bpm` is probably dragged down by the same false steps, since each splits the fit into
shorter segments with levels of their own. That is a guess, untested.
