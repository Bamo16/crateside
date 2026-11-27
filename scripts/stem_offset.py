"""Align an added source to a track's mix by their shared audio, as multicam sync does.

    uv run --no-project --with numpy --with scipy --with soundfile python scripts/stem_offset.py \
        <mix> <added-source> [--window 5] [--table]

Offset d means added[n] matches mix[n + d]: d > 0 says the mix has d more samples before the same
sound. The reference is always the mix: no stem is needed, and none helps. Each non-silent window of
the added source is whitened-cross-correlated (PHAT) against the whole mix, and its confidence is the
peak's height over that window's own correlation noise (peak-to-sidelobe ratio, PSR). A window under MIN_PSR is unmatched content: reported, never
disagreement. Only confident windows vote, and they give the verdict: steady, edit, drift (refused)
or not the same recording (refused). Reads only.
"""

import argparse

import numpy as np
import soundfile as sf
from scipy import fft, signal

MIN_PSR = 12.0  # a window whose peak is under this many noise widths is unmatched content
MIN_CONFIDENT = 3  # fewer confident windows than this: not the same recording
MIN_SHARE = 0.25  # a smaller share of the added source's windows confident: not the same recording
AGREE_MS = 0.5  # two offsets within this many milliseconds are the same offset
STEADY_SHARE = 0.90  # share of confident windows on one offset for a steady verdict
RUN = 2  # consecutive windows an offset elsewhere in the mix needs to count as an edit
DRIFT_MS = 1.0  # offsets moving this far along a straight line is a drift
DRIFT_WINDOWS = 4  # windows a line needs before it can be called a drift
JOIN_MS = 45.0  # consecutive windows this close in offset belong to one run
SPEED_FACTORS = ((1000, 1001), (1001, 1000), (24, 25), (25, 24))  # known speed changes, tried before refusing
NEAR_SECONDS = 1.0  # a window looks this far around the global offset first (loops repeat exactly)
SILENT_RMS = 1e-4
LOW_HZ, HIGH_HZ = 100, 10000


def load_mono(path):
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    return data.mean(axis=1), rate


class Reference:
    """The mix, transformed once so each window costs one more transform."""

    def __init__(self, mix, rate, longest):
        self.rate = rate
        self.length = len(mix)
        self.size = fft.next_fast_len(len(mix) + longest, real=True)
        self.spectrum = fft.rfft(mix, self.size, workers=-1)
        freqs = np.fft.rfftfreq(self.size, 1 / rate)
        self.band = (freqs >= LOW_HZ) & (freqs <= HIGH_HZ)

    def correlate(self, seg):
        """Lags and PHAT correlation; lag d means seg[n] matches mix[n + d]."""
        cross = self.spectrum * np.conj(fft.rfft(seg, self.size, workers=-1))
        cross /= np.abs(cross) + 1e-9 * np.abs(cross).max() + 1e-20
        cross[~self.band] = 0
        corr = fft.irfft(cross, self.size, workers=-1)
        lags = np.arange(self.size)
        lags[lags > self.length - 1] -= self.size
        valid = (lags > -len(seg)) & (lags < self.length)
        return lags[valid], corr[valid]


def psr(corr, index):
    """Peak height over the correlation's own noise: median and MAD-scaled width of every lag."""
    noise = np.median(corr)
    width = 1.4826 * np.median(np.abs(corr - noise)) + 1e-30

    return float((corr[index] - noise) / width)


def best(lags, corr, centre=None, reach=0):
    """Index of the highest peak, within `reach` of `centre` when given."""
    if centre is None:
        return int(np.argmax(corr))
    inside = np.flatnonzero(np.abs(lags - centre) <= reach)
    if len(inside) == 0:
        return int(np.argmax(corr))

    return int(inside[np.argmax(corr[inside])])


def global_offset(added, ref):
    lags, corr = ref.correlate(added)
    return int(lags[int(np.argmax(corr))])


class Window:
    def __init__(self, start):
        self.start = start  # seconds into the added source
        self.silent = False
        self.near = (0, 0.0)  # offset and PSR of the best peak around the global offset
        self.far = (0, 0.0)  # the same for the best peak anywhere in the mix
        self.kind = "weak"  # near, edit, weak, silent

    @property
    def offset(self):
        return self.far[0] if self.kind == "edit" else self.near[0]

    @property
    def confident(self):
        return self.kind in ("near", "edit")


def measure(added, ref, seconds, d0):
    rate = ref.rate
    size = int(seconds * rate)
    rows = []
    starts = list(range(0, max(1, len(added) - size + 1), size))
    if len(added) > size and starts[-1] + size < len(added):
        starts.append(len(added) - size)
    for start in starts:
        row = Window(start / rate)
        rows.append(row)
        seg = added[start : start + size]
        if np.sqrt(np.mean(seg**2)) < SILENT_RMS:
            row.silent, row.kind = True, "silent"
            continue

        lags, corr = ref.correlate(seg)
        near = best(lags, corr, start + d0, int(NEAR_SECONDS * rate))
        far = best(lags, corr)
        row.near = (int(lags[near]) - start, psr(corr, near))
        row.far = (int(lags[far]) - start, psr(corr, far))
        if row.near[1] >= MIN_PSR:
            row.kind = "near"

    mark_edits(rows, rate)
    return rows


def samples_of(ms, rate):
    return ms * rate / 1000


def mark_edits(rows, rate):
    """A weak run whose best peak elsewhere in the mix repeats is an edit, unless the main offset resumes
    after it: then the mix repeats that material, and the run is unmatched content."""
    awake = [r for r in rows if not r.silent]
    i = 0
    while i < len(awake):
        row = awake[i]
        if row.kind != "weak" or row.far[1] < MIN_PSR:
            i += 1
            continue

        j = i
        while (
            j + 1 < len(awake)
            and awake[j + 1].kind == "weak"
            and awake[j + 1].far[1] >= MIN_PSR
            and abs(awake[j + 1].far[0] - awake[j].far[0]) <= samples_of(AGREE_MS, rate)
        ):
            j += 1
        bracketed = (
            i > 0 and awake[i - 1].kind == "near" and j + 1 < len(awake) and awake[j + 1].kind == "near"
        )
        if j - i + 1 >= RUN and not bracketed:
            for member in awake[i : j + 1]:
                member.kind = "edit"
        i = j + 1


def segments(live, rate):
    """Consecutive confident windows whose offsets stay within JOIN_MS of the previous one."""
    found = []
    for row in live:
        if found and abs(row.offset - found[-1][-1].offset) <= samples_of(JOIN_MS, rate):
            found[-1].append(row)
        else:
            found.append([row])
    return found


def drift_of(live, rate):
    """(ppm, samples per second) of the first segment lying on a line that moves; else None."""
    for seg in segments(live, rate):
        if len(seg) < DRIFT_WINDOWS:
            continue

        times = np.array([r.start for r in seg])
        ds = np.array([r.offset for r in seg], dtype=float)
        slope, icpt = np.polyfit(times, ds, 1)
        onto = np.abs(ds - (slope * times + icpt)) <= samples_of(AGREE_MS, rate)
        if (
            onto.mean() >= STEADY_SHARE
            and abs(slope) * (times.max() - times.min()) >= samples_of(DRIFT_MS, rate)
        ):
            return slope / rate * 1e6, slope
    return None


def clusters(live, rate):
    """Confident windows sharing one offset, biggest first: (offset, windows)."""
    found = []
    for row in sorted(live, key=lambda r: r.offset):
        if found and row.offset - found[-1][-1].offset <= samples_of(AGREE_MS, rate):
            found[-1].append(row)
        else:
            found.append([row])
    out = [(int(np.median([r.offset for r in c])), sorted(c, key=lambda r: r.start)) for c in found]
    return sorted(out, key=lambda c: -len(c[1]))


def spans_of(rows, seconds, total, wanted):
    spans = []
    for row in rows:
        if wanted(row):
            end = min(row.start + seconds, total)
            if spans and row.start <= spans[-1][1] + 1e-6:
                spans[-1][1] = max(spans[-1][1], end)
            else:
                spans.append([row.start, end])
    return [(a, b) for a, b in spans]


def mix_gaps(live, seconds, rate, added_seconds, mix_seconds):
    """Spans of the mix with no confident window of the added source on them."""
    covered = sorted(
        (r.start + r.offset / rate, min(r.start + seconds, added_seconds) + r.offset / rate) for r in live
    )
    spans, at = [], 0.0
    for a, b in covered:
        if a - at > 0.5:
            spans.append((at, a))
        at = max(at, b)
    if mix_seconds - at > 0.5:
        spans.append((at, mix_seconds))
    return spans


def verdict(rows, rate, seconds):
    live = [r for r in rows if r.confident]
    awake = [r for r in rows if not r.silent]
    counted = f"{len(live)} of {len(awake)} windows confident"
    if len(live) < MIN_CONFIDENT or len(live) / max(1, len(awake)) < MIN_SHARE:
        return "NOT THE SAME RECORDING", live, None, counted

    if (drift := drift_of(live, rate)) is not None:
        ppm, slope = drift
        return "DRIFT", live, None, f"{counted}; {ppm:+.0f} ppm ({slope:+.1f} samples/s)"

    groups = clusters(live, rate)
    top, members = groups[0]
    if len(members) / len(live) >= STEADY_SHARE:
        return "STEADY", members, top, f"{counted}, {len(members)} on {top}"

    spans = []
    for row in live:
        key = next(o for o, c in groups if row in c)
        if spans and spans[-1][2] == key:
            spans[-1][1] = row.start
            spans[-1][3] += 1
        else:
            spans.append([row.start, row.start, key, 1])
    if len({s[2] for s in spans if s[3] >= RUN}) >= 2:
        text = "; ".join(f"{s[2]} over {s[0]:g}-{s[1] + seconds:g} s" for s in spans if s[3] >= RUN)
        return "EDIT", live, None, f"{counted}: {text}"

    return "NOT THE SAME RECORDING", live, None, f"{counted}, sharing no offset, run or line"


def fmt_spans(spans):
    return ", ".join(f"{a:g}-{b:g} s" for a, b in spans) or "none"


def analyse(added, mix, rate, seconds):
    d0 = global_offset(added, Reference(mix, rate, len(added)))
    rows = measure(added, Reference(mix, rate, int(seconds * rate)), seconds, d0)
    name, voters, offset, why = verdict(rows, rate, seconds)

    return d0, rows, name, voters, offset, why


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mix")
    parser.add_argument("added")
    parser.add_argument("--window", type=float, default=5)
    parser.add_argument("--table", action="store_true", help="also print every window")
    args = parser.parse_args()

    mix, rate = load_mono(args.mix)
    added, rate2 = load_mono(args.added)
    if rate != rate2:
        raise SystemExit(f"sample rates differ: {rate} and {rate2}")

    d0, rows, name, voters, offset, why = analyse(added, mix, rate, args.window)
    if name in ("NOT THE SAME RECORDING", "EDIT"):
        for up, down in SPEED_FACTORS:
            stretched = signal.resample_poly(added, up, down).astype(np.float32)
            d1, rows1, name1, voters1, offset1, _ = analyse(stretched, mix, rate, args.window)
            if name1 == "STEADY":
                pace = "fast" if up > down else "slow"
                d0, rows, voters, offset = d1, rows1, voters1, offset1
                name = "DRIFT"
                why = (
                    f"runs {abs(up / down - 1) * 100:.2f}% {pace} against the mix "
                    f"(resampled by {up}/{down} it reads steady at {offset}); use the audio release"
                )
                break

    print(f"mix {len(mix) / rate:.3f} s  added {len(added) / rate:.3f} s  rate {rate}  global offset {d0}")
    if args.table:
        for r in rows:
            if r.silent:
                print(f"  {r.start:7.1f} s  silent")
                continue

            print(
                f"  {r.start:7.1f} s  near d {r.near[0]:9d} psr {r.near[1]:6.1f}"
                f"  far d {r.far[0]:9d} psr {r.far[1]:6.1f}  {r.kind}"
            )

    strong = [r.near[1] for r in rows if r.kind == "near"]
    weak = [r.near[1] for r in rows if r.kind == "weak"]
    print(
        f"psr: weakest confident {min(strong, default=float('nan')):.1f}, "
        f"strongest unmatched (at the offset) {max(weak, default=float('nan')):.1f}, threshold {MIN_PSR:g}"
    )
    if offset is not None:
        print(f"{name} at {offset} samples ({offset / rate * 1000:+.3f} ms): {why}")
    else:
        print(f"{name}: {why}")
    total = len(added) / rate
    print(f"unmatched in added: {fmt_spans(spans_of(rows, args.window, total, lambda r: r.kind == 'weak'))}")
    elsewhere = [r for r in rows if r.confident and r not in voters]
    if name == "STEADY" and elsewhere:
        spans = spans_of(elsewhere, args.window, total, lambda r: True)
        print(f"also matching elsewhere in the mix (repeated material, not voting): {fmt_spans(spans)}")
    if name in ("STEADY", "EDIT"):
        print(f"mix not covered: {fmt_spans(mix_gaps(voters, args.window, rate, total, len(mix) / rate))}")


if __name__ == "__main__":
    main()
