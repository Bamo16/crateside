# crateside's transients against Live's own

*Note, 2026-10-02, frozen. For [#54](https://github.com/Bamo16/crateside/issues/54). Cited by
[ADR 0043](../adr/0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md).*

A sidecar crateside writes needs transients. Live never detects them in a file it did not write, and
an empty list shows none and cannot be reset, so crateside detects its own (`OnsetDetector`). This
note measures it against the transients Live detected for the same audio.

## Ground truth

Every sidecar under Mashup Samples and Sample Staging whose `OriginalFileSize` still matches its
audio file: **958 of 975** (measured). The other 17 describe audio that has changed since Live
analysed it, *Many Men*'s pair among them. Their waveforms disagree with the file now on disk.
Decoding with crateside's `FfmpegAudioDecoder` reproduces Live's waveform bit for bit on every
current file checked (two fixture WAVs, seven staged and finished FLACs), so a mismatch is a stale
sidecar, not a decode.

**What Live's transients look like** (measured, 958 files):

- a median of **244 per minute** (10th percentile 126, 90th 338);
- almost never closer than **77 ms** (1st percentile of the gaps: 3,402 frames), with 15 exact
  repeats in the whole library;
- energies scaled so the strongest in each file is exactly 1.0 (one per file); median 0.27.

**Where Live puts one** (measured on 18 files, 12,327 transients, against a 1.5 ms loudness
envelope): not at the very start of the rise. It sits a median 15% of the way up on drums, 22% on
instrumentals and 35% on vocals, which rise more slowly. The loudness floor before it is 19 to
32 ms earlier, and the peak after it 14 to 17 ms later.

## The detector

Spectral flux on the mono mix: a 1,024-sample Hann window every 256 samples, log-compressed
magnitudes up to 8 kHz, each compared against the previous frame widened by one bin. A peak counts
when it is the highest within 30 ms, stands 0.02 of the file's strongest above the average of the
surrounding 100 ms, and is at least 70 ms from the last one. Each is then placed on the loudness
envelope: the floor up to 40 ms before the peak, the peak up to 20 ms after the flux frame's centre,
and the transient **where the rise crosses halfway between them in decibels**. Energy is the flux
peak over the file's strongest.

## Scoring

Each of Live's transients is matched to the nearest unused one of crateside's within **25 ms**.
**Recall** is the share of Live's that are matched, **precision** the share of crateside's, and the
**offset** is crateside's position minus Live's for each match.

## Results, all 958 files

| stems | files | recall, median | 10th percentile | files at 81% or more | precision, median | count against Live's | offset, median | absolute offset, median |
|---|---|---|---|---|---|---|---|---|
| all | 958 | 80.0% | 62.3% | 49% | 67.7% | 1.23 | +0.73 ms | 4.47 ms |
| instrumental | 451 | **90.7%** | **81.0%** | 90% | 82.1% | 1.13 | +1.18 ms | 3.83 ms |
| drums | 16 | **93.9%** | 84.1% | 100% | 87.5% | 1.10 | +0.20 ms | 1.16 ms |
| vocal | 446 | 69.1% | 57.7% | 3% | 51.7% | 1.40 | −1.04 ms | 5.85 ms |
| other (karaoke, kept sources) | 45 | 90.4% | 73.2% | 78% | 77.0% | 1.20 | +2.24 ms | 5.24 ms |

**Against the bar** (the lost librosa prototype: 81–99% of Live's within 25 ms, and a median
offset under 5 ms): instrumentals and drums meet it, with most files above 81% and timing well
inside. Vocals do not.

## What the vocal gap is

Of all the transients missed, 62% have one of crateside's 25 to 75 ms away. On vocals those are
mostly early (51,292 early against 11,603 late, median −33.5 ms). So the misses are mostly
**placement**, not detection: crateside marks the syllable and Live marks it about 30 ms later.

Every placement tried moved the vocal misses without closing them, on 33 vocal stems:

| change | vocal recall, median |
|---|---|
| as shipped | 68.8% |
| peak searched 40 ms after the onset, not 20 | 71.1%, misses now evenly early and late |
| peak searched 60 ms after | 62.5%, misses now late |
| a fixed linear share of the rise (10%, 25%, 30%, 50%) | 61–69% |
| the steepest point of the rise | 70% |

On vocals Live's transient is not tied to one point of the loudness envelope this detector can
find. Other things tried, on 30 files of all kinds:
- a 2,048-sample window, which lost 7 points of recall;
- frequency ceilings of 1, 2 and 4 kHz, which raised vocal recall a little and cost instrumentals
  more;
- a smaller minimum gap.

None changed the picture.

## What follows

- **Automatic lending ships switched off** (ADR 0043): vocals are the stems most often given a
  borrowed warp, and they are where the detector falls short.
- **Instrumentals and drums are good enough to write**, which is where the user places warp markers
  on transients.
- A better vocal rule would be the next step: perhaps Live marks a vocal at its pitch onset
  rather than its loudness. That is a guess and has not been tested.
