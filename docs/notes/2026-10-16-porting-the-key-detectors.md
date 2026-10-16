# Porting the key detectors: what Essentia's `edma` chain does, measured from outside

*Note, 2026-10-16, frozen. For [#113](https://github.com/Bamo16/crateside/issues/113) (port the
chosen key detectors to .NET), under [ADR 0055](../adr/0055-crateside-owns-the-key-judge-and-its-ledger.md)
and its addendum naming the set. The bake-off it checks against is
[2026-10-15-the-detector-bake-off.md](2026-10-15-the-detector-bake-off.md).*

## Why measured, not read

Essentia is AGPL-3.0, and the addendum says the `edma` port is written from the published method
and a parity check, not from Essentia's source. Its documentation gives each algorithm's
parameters but not the arithmetic: how SpectralWhitening builds its envelope, how HPCP weights
subharmonics, and what `KeyExtractor` sets inside itself are not on its pages. Each was found by
feeding Essentia's own algorithms chosen inputs in a Linux container (Essentia 2.1-beta6-dev,
madmom 0.17.dev0, the bake-off's versions) and fitting what came out. Nothing was read from its
source except the `edma` profile values, taken as data, as the addendum allows.

madmom is BSD, so its front end was ported from its source; its model files are CC BY-NC-SA 4.0
and the converted network carries that licence (`src/Crateside.Core/KeyDetection/Madmom/LICENSE`).

## `KeyExtractor(profileType="edma")`, as reproduced

A chain built from Essentia's standard-mode algorithms matched `KeyExtractor`'s key and strength to
the last printed digit on all 37 corpus tracks. Its settings, found by searching combinations
against `KeyExtractor`'s output:

| stage | setting |
|---|---|
| frames | 4096 samples, hop 4096; the first starts 2048 before the audio, and frames continue while their start is before the end (`ceil((n + 2048) / 4096)` frames) |
| window | symmetric Hann, scaled to sum to 2 |
| spectrum | magnitude, 2049 bins |
| peaks | the 60 largest local maxima from bin 3 to bin 326 (25 to 3500 Hz), threshold 1e-4, parabolic interpolation on the linear magnitude; the range's first and last bins count as peaks when they beat their one inner neighbour, uninterpolated; peaks above 3500 Hz after interpolation are dropped |
| whitening | on (see below) |
| HPCP | 12 bins from A, reference 440 Hz, 4 subharmonics, cosine window one semitone wide, no normalisation; the band preset makes no difference here |
| averaging | the mean over frames, scaled to its maximum, bins below 0.2 set to 0 |
| key | Pearson's r against the `edma` major and minor profiles rotated to each tonic; the best of 24, its r the strength; no polyphonic profile |

Without whitening, 15 of the 37 corpus keys change, so it is not optional.

**HPCP**, from single synthetic peaks: a peak's power (magnitude squared) is credited to the pitch
class of the peak and of its subharmonics f/2 to f/5. Subharmonic h is weighted `min(1, 2 / log2 h)`
(1, 1, 1, 1, 0.861 for h = 1 to 5, and 0.774, 0.712, 0.667, 0.631 for h = 6 to 9, which fit the
same rule). Weights of subharmonics that land on the same pitch class add as amplitudes before
squaring: a peak at A with h = 1, 2 and 4 contributes 9, not 3. Separate peaks add as powers. The
window weight is `cos(π d)` for the distance d in semitones to the bin's centre.

**SpectralWhitening**, from flat spectra, single-bin bumps of 0.5 to 60 dB and single probe peaks:

- An envelope is computed at breakpoints every 100 Hz and interpolated linearly in dB between them.
- At each breakpoint it is the contraharmonic mean of the power spectrum under a kernel:
  `Σ w P² / Σ w P` (the bump-height sweep fits this to four digits from 5 to 30 dB, where means in
  dB, amplitude or power do not). The kernel is `(1 − |j − c| / W)⁴` over bins `lo` to `hi`, with
  `c` and `W` their midpoint and half-width. For a breakpoint at F Hz, `lo = round(0.66 F / Δ)` and
  `hi = round(1.58 F / Δ)`, Δ being the bin width; the breakpoints at 0 and 100 Hz are narrower
  than that (bins 0 to 5 and 5 to 15), measured and tabulated.
- Each peak becomes `min(peak dB − envelope dB, 0) − f / 200` dB; a peak 30 dB or more below the
  envelope is set to −200 dB first. Dips in the spectrum do not move the envelope.

Reimplemented this way in NumPy, whitening matched Essentia within 1.5e-5 dB on all 3183 frames
of one track, with the same peaks zeroed.

## madmom

The network was converted to ONNX in the same container (`Madmom/convert.py`): kernels flipped,
since madmom convolves and ONNX correlates; batch norm folded into each convolution. ONNX Runtime's
probabilities matched madmom's within 4e-7 on the 37 corpus tracks. The front end (8192-sample
Hann frames at 5 per second, 24-band log filterbank from 65 to 2100 Hz, `log10(1 + x)`) follows
madmom's source; its filterbank has 105 bands.

## Parity

Every track decoded to 44.1 kHz mono float by the same ffmpeg for both sides.

| set | tracks | madmom keys equal | `edma` keys equal | largest confidence difference |
|---|---|---|---|---|
| keydetect-harness corpus | 37 | 37 | 37 | madmom 5e-6, `edma` 5e-6 |

The container also reproduced the bake-off CSV's `madmom` and `ess_edma` columns on all 37, so the
CSV is a fair yardstick for the port.

## Speed

Per track, on the corpus (170 to 370 s songs), Release build, one thread:

| | median | worst |
|---|---|---|
| madmom (ONNX Runtime, CPU) | 0.51 s | 0.95 s |
| `edma` | 0.72 s | 1.33 s |
| keydetect (ffmpeg) | 1.7 s on one 295 s track | |

Against 20 s for madmom in NumPy and 0.25 s for Essentia in the bake-off.
