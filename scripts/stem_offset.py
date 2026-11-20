"""Measure where an added source sits against a track's mix or stem.

    uv run --no-project --with numpy --with scipy --with soundfile python scripts/stem_offset.py \
        <mix-or-stem> <added-source> [--window 10]

Offset d means added[n] matches mix[n + d]: d > 0 says the mix has d more samples before the same
sound. The whole-file offset is the peak of a whitened (PHAT) cross-correlation over both files.
Each window of the added source is then searched against the whole mix, so an edit of any size
shows up as a jump rather than as a miss, and the windows' offsets give the verdict: steady,
drift, edit or no match (the rules of the added-source offset ADR). r is the Pearson correlation
of the aligned audio, kept as a hint of how alike the masters are; it does not decide anything.
Reads only.
"""

import argparse

import numpy as np
import soundfile as sf
from scipy import signal

DRIFT_SAMPLES = 10  # the offset moving this far along a straight line is a drift
MIN_BAND_R = 0.30  # best band's r below this: the files are not the same recording
BANDS = ((100, 1000), (1000, 4000), (4000, 12000))

AGREE = 3  # samples within which two windows count as the same offset
STEADY_SHARE = 0.75  # share of windows that must agree for a single offset


def load_mono(path):
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    return data.mean(axis=1), rate


def phat(a, b, rate):
    """Lags and whitened correlation of a against b; lag d means a[n] matches b[n + d]."""
    size = 1 << int(np.ceil(np.log2(len(a) + len(b))))
    cross = np.fft.rfft(b, size) * np.conj(np.fft.rfft(a, size))
    cross /= np.abs(cross) + 1e-9 * np.abs(cross).max() + 1e-20
    freqs = np.fft.rfftfreq(size, 1 / rate)
    cross[(freqs < 100) | (freqs > 10000)] = 0
    corr = np.fft.irfft(cross, size)
    lags = np.arange(size)
    lags[lags > len(b) - 1] -= size
    return lags, corr


def peak(lags, corr):
    return int(lags[int(np.argmax(corr))])


def pearson(a, b, d):
    """Pearson r of a[n] against b[n + d] over their overlap."""
    start = max(0, -d)
    end = min(len(a), len(b) - d)
    if end - start < 4410:
        return float("nan")

    return float(np.corrcoef(a[start:end], b[start + d : end + d])[0, 1])


def band_r(added, mix, rate, d):
    """Pearson r of the aligned files in each band; masters that differ in EQ still agree in one."""
    out = []
    for lo, hi in BANDS:
        sos = signal.butter(4, [lo, hi], btype="band", fs=rate, output="sos")
        out.append(pearson(signal.sosfilt(sos, added), signal.sosfilt(sos, mix), d))
    return out


def windows(added, mix, rate, seconds, d0, near=1.0):
    """Each window looks within `near` s of d0 first, and the whole mix only when that misses.

    A loop repeats exactly, so a whole-mix search alone lands on another repeat of it.
    """
    size = int(seconds * rate)
    rows = []
    for start in range(0, len(added) - size // 2, size):
        seg = added[start : start + size]
        if np.sqrt(np.mean(seg**2)) < 1e-4:
            rows.append((start / rate, None, None))
            continue

        reach = int(near * rate)
        lo = max(0, start + d0 - reach)
        lag = peak(*phat(seg, mix[lo : start + d0 + len(seg) + reach], rate)) + lo
        if abs(lag - start - d0) > AGREE:
            lag = peak(*phat(seg, mix, rate))

        rows.append((start / rate, lag - start, pearson(seg, mix, lag)))
    return rows


def clusters(rows):
    """Windows sharing one offset, biggest first: (offset, first s, last s, count)."""
    found = []
    for t, d, _ in sorted((r for r in rows if r[1] is not None), key=lambda r: r[1]):
        if found and d - found[-1][-1][1] <= AGREE:
            found[-1].append((t, d))
        else:
            found.append([(t, d)])
    out = [
        (int(np.median([d for _, d in c])), min(t for t, _ in c), max(t for t, _ in c), len(c))
        for c in found
    ]
    return sorted(out, key=lambda c: -c[3])


def verdict(rows, rate):
    live = [(t, d) for t, d, _ in rows if d is not None]
    if len(live) < 3:
        return "too little audio to judge"

    best = clusters(rows)[0]
    if best[3] / len(live) >= STEADY_SHARE:
        gaps = [t for t, d in live if abs(d - best[0]) > AGREE]
        note = f"; no match at {', '.join(f'{t:g}s' for t in gaps)}" if gaps else ""
        return f"STEADY at {best[0]} samples ({best[3]} of {len(live)} windows){note}"

    times = np.array([t for t, _ in live])
    ds = np.array([d for _, d in live], dtype=float)
    near = np.abs(ds - np.median(ds)) < rate  # a stray far peak must not bend the line
    slope, icpt = np.polyfit(times[near], ds[near], 1) if near.sum() >= 3 else (0.0, 0.0)
    onto = np.abs(ds - (slope * times + icpt)) <= AGREE
    span = abs(slope) * (times.max() - times.min())
    if len(live) >= 6 and onto.mean() >= STEADY_SHARE and span >= DRIFT_SAMPLES:
        return f"DRIFT {slope:.3f} samples/s ({slope / rate * 1e6:.1f} ppm), {icpt:.0f} samples at 0 s"

    runs = [c for c in clusters(rows) if c[3] >= 2]
    if len(runs) >= 2 and sum(c[3] for c in runs) / len(live) >= STEADY_SHARE:
        return "EDIT: " + "; ".join(
            f"{c[0]} samples over {c[1]:g}-{c[2]:g}s" for c in sorted(runs, key=lambda c: c[1])
        )

    return f"NO MATCH: the best offset, {best[0]} samples, holds in {best[3]} of {len(live)} windows"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mix")
    parser.add_argument("added")
    parser.add_argument("--window", type=float, default=10)
    args = parser.parse_args()

    mix, rate = load_mono(args.mix)
    added, rate2 = load_mono(args.added)
    if rate != rate2:
        raise SystemExit(f"sample rates differ: {rate} and {rate2}")

    d0 = peak(*phat(added, mix, rate))
    print(f"mix {len(mix) / rate:.3f}s  added {len(added) / rate:.3f}s  rate {rate}")
    print(f"whole-file offset {d0} samples ({d0 / rate * 1000:.3f} ms)  r {pearson(added, mix, d0):+.3f}")
    bands = band_r(added, mix, rate, d0)
    print("band r " + "  ".join(f"{lo}-{hi} Hz {r:+.3f}" for (lo, hi), r in zip(BANDS, bands)))
    print(f"best band r {max(bands):+.3f}: {'enough' if max(bands) >= MIN_BAND_R else 'too low'} (needs {MIN_BAND_R})")
    rows = windows(added, mix, rate, args.window, d0)
    for t, d, r in rows:
        print(f"  {t:7.1f}s  silent" if d is None else f"  {t:7.1f}s  d {d:9d}  ({d - d0:+7d})  r {r:+.3f}")

    print(verdict(rows, rate))


if __name__ == "__main__":
    main()
