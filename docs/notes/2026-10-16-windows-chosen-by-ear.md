# Windows chosen by ear: two runs of "Judge from these bars"

*Note, 2026-10-16, frozen. Written for keydetect-harness's report 05, as worked examples of the
form from [#116](https://github.com/Bamo16/crateside/issues/116) (judge from these bars), run
while re-judging the overruled verdicts of
[#117](https://github.com/Bamo16/crateside/issues/117). Decides nothing. Every figure is read from
the ledger rows the runs recorded; the maintainer chose the windows and does not trust their own
ear for keys, only for where a section starts and ends.*

## fusq – perfume! (run 540)

**The windows.** One, bars 1–64 of the maintainer's warp (37.8–158.7 s), leaving out the
intro (a wind-up music machine and synth, until about 0:23) and the outro (after 2:40). The
maintainer's note to the judge: they were not sure whether the intro and outro match the rest of
the song's key.

| | keydetect | madmom | edma |
|---|---|---|---|
| whole file | C♯ minor, r 0.79 | E major, 0.693 | C♯ minor, 0.896 |
| bars 1–64 | C♯ minor, r 0.787 | E major, 0.661 | C♯ minor, 0.892 |

**What it showed.** Nothing moved: the window is most of the song, so the detectors read it as
they read the whole file. The question the maintainer wanted answered, whether the intro and
outro were pulling the reading, has the answer no. The plain re-judge (run 535) and this one both
recorded **C♯ minor, MEDIUM**, the same as the 2026-09-20 verdict.

**The dissent.** Hooktheory's TheoryTab names F♯ minor for its one section, `Instrumental`, which
is a four-bar figure (`i6 iv7sus2 v viio/v iv i6 ii io64 v V`) with no votes and no analysis. The
judge's rule caps the verdict at MEDIUM for any authoritative dissent, so the cap is what holds
the verdict down, not the chord evidence (G♯/G♯m resolving to C♯m four times; F♯m 4 of 64 chords,
Bm 1). The harness's yardstick uses that F♯ minor as perfume!'s truth; the maintainer does not
think a one-section entry is corpus-grade. Filed as
[#122](https://github.com/Bamo16/crateside/issues/122) (weigh how much a Hooktheory entry covers
before it caps a verdict).

**A caution about the mashup.** The maintainer once made a mashup (*50 Scent*, with *In Da Club*,
which is C♯ minor) from this track's instrumental. They picked the vocal because the sample was
tagged C♯ minor, so the mashup shows what the tag was, not that the tag is right, and hip-hop
vocals sit on most keys. It is not evidence either way.

## Houshou Marine – A Horny Money World (run 541)

**The windows.** Two, both labelled *Chorus*: bars 33–48 (60.1–86.6 s) and 82–96 (141.2–166.0 s).
The maintainer's recollection is that only the two choruses carry much harmonic content. No note
to the judge was given.

| | keydetect | madmom | edma |
|---|---|---|---|
| whole file | C♯ minor, r 0.766 | C♯ minor, 0.590 | G♯ major, 0.819 |
| chorus, 60–87 s | C♯ minor, r 0.756 | G♯ minor, 0.616 | G♯ major, 0.791 |
| chorus, 141–166 s | C♯ minor, r 0.737 | C♯ minor, 0.664 | G♯ major, 0.784 |

**What it showed.** keydetect says C♯ minor everywhere, edma says G♯ major everywhere, and
madmom wavers between them. The windows did not settle the detectors; they kept the disagreement
inside the choruses. What settled it was the chord stream, in the whole track and in both windows:
G♯ is a major chord that resolves into C♯m, a V–i cadence, so the detectors that name G♯ are
reading the dominant as the tonic. The judge also found a chord-recogniser page whose capo-4
guitar shapes transpose to the same C♯m–G♯–A–D♯m7♭5. No authoritative source covers this song, so
the verdict is **C♯ minor, MEDIUM**, with a section for each chorus (both C♯ minor).

**Why it was run.** It is the case that started #116 (ADR 0055's windows addendum): withheld on a
three-way split over the whole file, then C♯ minor from the choruses when the detectors were run
over them by hand. This is the first run of the same idea through the form, and it agrees with
the by-hand reading.

## Run Away (Real McCoy): no window

The maintainer listened to the whole track and heard nothing to exclude, so there is no ranged
run to report. The plain re-judge recorded **A♯ minor, MEDIUM** (it was C♯ major HIGH). All three
detectors say C♯ major, which is its relative major; the judge sided with Hooktheory's chorus,
B♭ minor, and the loop's A♯m with F♯, C♯ and G♯ as its VI, III and VII. It is a case for a
different kind of help from a window: the detectors agree and are all on the wrong member of the
pair.

## Observations for the report

- **From two runs, a window chosen by ear seems to help where the rest of the track is dominated by something other than the harmony**
  (Horny Money World: drums, bass and noise outside the choruses), not where it is most of the
  song (perfume!).
- **The detectors still disagree inside a window.** edma's G♯ major held across both choruses;
  the cure was the chord stream, not a cleaner reading.
- **The judge's prompt weighs a window below the whole track** (ADR 0055's last addendum, from
  report 05), and both verdicts above are the whole-track key with the window as support.
