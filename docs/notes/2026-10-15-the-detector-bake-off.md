# The detector bake-off: Essentia, madmom and libKeyFinder against keytag's three

*Note, 2026-10-15, frozen. For [#109](https://github.com/Bamo16/crateside/issues/109) (the detector
bake-off) under [#108](https://github.com/Bamo16/crateside/issues/108); cited by
[ADR 0055](../adr/0055-crateside-owns-the-key-judge-and-its-ledger.md), whose addendum decides
from it. Every reading is in [the CSV beside this note](2026-10-15-the-detector-bake-off.csv).*

## What was run

| yardstick | songs | "right" means |
|---|---|---|
| keydetect-harness `corpus.yaml` | 37 | the track's `primary` or one of its `acceptable` keys; a relative key is a miss |
| the ledger's human-sourced songs | 221 of 227 | the key an `authoritative` or `corroborating` source stated (all agreeing), same tonic and quality |

The ledger set is the one in
[the scoring note](2026-10-15-the-detectors-and-sources-scored-across-the-ledger.md) (227 songs).
Three have no audio (two removed videos, one with no source URL) and three have no CREMA
reading in the ledger, which leaves 221 with every detector.

- **Audio:** full mixes, never stems. The harness corpus is its local FLACs; the ledger songs were
  fetched again with `stemforge-cli download --format flac` from the ledger's `source_url`, because
  467 of the 519 original files were gone. The mix is the one the URL serves now, not byte-identical
  to what keytag read. Full mix against stem was not re-checked.
- **keytag's three:** on the corpus, `keytag-detect` was run on each file (ffmpeg
  `9.0-NoMercy-MediaServer`). On the ledger the recorded readings are used as they stand (mostly ffmpeg
  `8.1.2-NoMercy-MediaServer`, some `9.0`, per the ledger's version record). The harness's `analysis_segment` is unused (no track has one).
- **The candidates**, on 44.1 kHz mono PCM decoded by ffmpeg, whole track, in a Linux container
  (none of them builds or installs on this Windows machine): Essentia 2.1-beta6-dev
  `KeyExtractor` with `edma`, `bgate` and `temperley`, plus `edmm` and `shaath` for completeness;
  madmom 0.17.dev0 `CNNKeyRecognitionProcessor`; libKeyFinder 2.2.8 (`keyOfAudio`, default profile).
- CPU only; nothing here used the GPU or Claude.

Neither yardstick is independent of the old detectors: the ledger's human sources were chosen by a
judge that had keytag's three in view. That leans toward keytag's three, not away from them, since
the candidates were never in that room. This was reasoned, not measured.

## Each detector alone

| | corpus (37) | ledger (221) | ledger misses: relative / fifth / parallel / other |
|---|---|---|---|
| ffmpeg `keydetect` | **81%** | 71% | 22 / 22 / 7 / 13 |
| CREMA | 68% | 61% | 14 / 35 / 7 / 30 |
| librosa K-S | 46% | 57% | 20 / 27 / 11 / 37 |
| Essentia `edma` | 70% | 73% | 15 / 22 / 3 / 19 |
| Essentia `bgate` | 68% | 69% | 17 / 25 / 5 / 21 |
| Essentia `shaath` | 70% | 70% | 16 / 25 / 6 / 19 |
| Essentia `edmm` | 43% | 52% | 46 / 15 / 22 / 24 |
| Essentia `temperley` | 43% | 49% | 54 / 17 / 2 / 40 |
| madmom CNN | 65% | **74%** | 24 / 11 / 6 / 16 |
| libKeyFinder | 59% | 64% | 26 / 31 / 5 / 18 |

On the ledger, madmom and `edma` are level with keydetect (paired bootstrap of the accuracy
difference, 1000 resamples: madmom +3.2 points, 95% interval −4 to +10; `edma` +2.3, −5 to +9).
On the corpus keydetect leads, but the corpus is 37 clear pop tracks and the intervals include
zero. `temperley` and `edmm` miss mostly by the relative key and are out.

## As keydetect's backer

The question of the scoring note: does the partner agreeing predict keydetect being right?
AUC is of "the partner agrees" against "keydetect is right" (0.5 is no information).

| partner | ledger: keydetect right when it agrees | when it disagrees | AUC (corpus / ledger) | right when keydetect is wrong (ledger, of 64) |
|---|---|---|---|---|
| **madmom** | 128/139 (92%) | 29/82 (35%) | 0.73 / **0.82** | **36** |
| Essentia `edma` | 133/155 (86%) | 24/66 (36%) | 0.67 / 0.75 | 29 |
| Essentia `bgate` | 125/144 (87%) | 32/77 (42%) | 0.71 / 0.75 | 28 |
| Essentia `shaath` | 127/149 (85%) | 30/72 (42%) | 0.71 / 0.73 | 28 |
| CREMA | 108/125 (86%) | 49/96 (51%) | 0.60 / 0.71 | 27 |
| libKeyFinder | 122/150 (81%) | 35/71 (49%) | 0.69 / 0.67 | 19 |
| librosa K-S | 101/120 (84%) | 56/101 (55%) | 0.43 / 0.67 | 25 |

madmom is the best backer on both yardsticks, and the only one that also supplies the answer
about as often as it flags the doubt (36 of keydetect's 64 misses). Against CREMA the AUC gain
is +0.11 on the ledger (95% interval +0.03 to +0.19); on the corpus it is +0.13 with an interval
that includes zero.

## Sets (keydetect plus partners)

"Backed" counts how many partners name keydetect's key. Vote is the plurality of the three, ties
to keydetect.

| partners | corpus AUC | ledger AUC | ledger vote | ledger keydetect right: backed by 2 / 1 / 0 |
|---|---|---|---|---|
| **`edma` + madmom** | 0.76 | **0.844** | 173/221 (78%) | 115/125 (92%) / 31/44 (70%) / 11/52 (21%) |
| CREMA + madmom | 0.72 | 0.843 | 170/221 (77%) | 97/101 (96%) / 42/62 (68%) / 18/58 (31%) |
| `bgate` + madmom | 0.76 | 0.835 | 167/221 (76%) | 114/123 (93%) / 25/37 (68%) / 18/61 (30%) |
| CREMA + librosa (keytag today) | 0.53 | 0.751 | 163/221 (74%) | 79/88 (90%) / 51/69 (74%) / 27/64 (42%) |

Of the single partners, madmom alone (AUC 0.82) is the best on the ledger. `edma` + madmom
against CREMA + librosa on the ledger: AUC +0.09 (95% interval +0.02 to +0.17), vote accuracy
+4.5 points (interval 0 to +9). On the corpus: AUC +0.23 (+0.04 to +0.45). `edma` + madmom and
CREMA + madmom are indistinguishable (difference +0.001, interval −0.04 to +0.04), as are `edma` +
madmom and `bgate` + madmom. The pair that includes `edma` is the pick for what it costs to port,
not for accuracy.

CREMA and librosa are the weakest pair on both yardsticks: where neither backs keydetect it is still
right 42% of the time, against 21% with `edma` + madmom, so they separate its good answers from its
bad ones less sharply.

## Runtime per track (a 170–370 s song)

| | time |
|---|---|
| ffmpeg `keydetect` | about 4 s (a 327 s track; 3.7 s wall) |
| Essentia `KeyExtractor`, any profile | 0.25 s median |
| libKeyFinder | 0.75 s |
| madmom CNN | 20 s median, 27 s worst, serial in numpy; more threads did not help |
| decoding to PCM | 3.5 s |

That is serial on one container; the parallel pass (6 at once) read 36 s median for madmom. madmom's
cost is NumPy convolutions; ONNX Runtime was not timed.

## Windows

All three take a time window the same way: the audio is sliced before the detector sees it, with
ffmpeg `-ss`/`-t` here. Checked on one track at 60–100 s: every detector returned a key. None
needs a windowing option of its own, so the port accepts a window by decoding only that range.

## Whether each can run in .NET

| detector | license | route |
|---|---|---|
| ffmpeg `keydetect` | FiLL's, in the NoMercy build | already an ffmpeg call; nothing to port |
| madmom CNN | code BSD; **model files CC BY-NC-SA 4.0** | ONNX. The net is plain: nine convolutions (one 5×5, seven 3×3, a 1×1 last), batch norm, 3 max-pools, averaged over frames; 2.7 MB of weights. Its input is a 24-bands-per-octave log spectrogram (8192-sample frames, 5 per second, 65–2100 Hz, `log(x+1)`). A converter exists (the `madmom-onnx` fork) |
| Essentia `edma` / `bgate` | **AGPL-3.0** (commercial license on request) | native C#: HPCP from spectral peaks, correlated against a profile. No .NET binding exists, and essentia.js has no KeyExtractor that I could confirm |
| libKeyFinder | **GPL-3.0** | a P/Invoke to a native build with FFTW is possible, or a third-party NuGet (`LibKeyFinderDotNet`, unlisted); not needed |
| CREMA, librosa K-S | ISC | TensorFlow and a Python venv today; not worth porting given the above |

The weights' license and Essentia's both bear on making crateside public
([#107](https://github.com/Bamo16/crateside/issues/107)); ADR 0055's addendum says how.

## Not measured

- Whether a ported detector reproduces its Python original. #113 checks that.
- Full mix against stem, and keydetect's 8.1.2 build against the 9.0 build on the ledger.
- The 495 confident-verdict songs the earlier note also used; no audio was fetched beyond the 227.
- madmom through ONNX Runtime: speed and parity.
- Any effect of bitrate: the ledger audio is the 258 kbps AAC stream re-encoded to FLAC.
