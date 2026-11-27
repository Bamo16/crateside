# 0108 - A new detector build is adopted by a fixed check

- Status: Accepted
- Date: 2026-11-19
- Extends [ADR 0066](0066-keydetect-has-its-own-pinned-build.md) (the pin and the re-read) and
  [ADR 0093](0093-the-filter-builds-are-fetched-by-pin-on-first-run.md) (the pin in code).
  First applied to keydetect 1.6.0, in [the note of 2026-11-19](../notes/2026-11-19-rules-first-on-keydetect-1.6.0.md).

## Context

keydetect and beatdetect are FiLL's filters, and each new release can move what crateside decides.
Until now every move to a new build (ADR 0066 to 1.2.1, ADR 0093 to 1.3.0 and 1.4.0, keydetect 1.6.0
on 2026-11-19) was decided ad hoc, with whatever measurement seemed right that day. This fixes the
check, so a new build is either pinned by a mechanical pass or held, and the maintainer is asked
only when a gate fails.

### What reads a detector's output

Read from the code on 2026-11-19. "Calibrated" means a threshold chosen on one build's output.

| consumer | reads | decides or shows | calibrated on a build | when the pin changes, today |
|---|---|---|---|---|
| rules-first verdicts, `VerdictRules` (ADR 0076) | keydetect `key` and `key_confidence` (`r`), with madmom and edma | **decides**: a `DecidedBy = Rules` verdict when the detectors and the pages settle a key; `r` at 0.80 (`ConfidentKeydetect`) | yes: keydetect 1.3.0, measured in [the note of 2026-11-03](../notes/2026-11-03-what-rules-can-rule-on-without-a-judge.md) and again on 1.6.0 | **nothing**: the evidence is re-read, the ruling stands (gap, [#207](https://github.com/Bamo16/crateside/issues/207)) |
| key tag written on a sample, `TaggingRunner` | the ledger's verdict | **decides** a file's key tag | no | **nothing**: only files with no key applied are touched (gap, [#208](https://github.com/Bamo16/crateside/issues/208)) |
| judge evidence, `JudgePrompt`, Evidence page | all of keydetect's output in the run's evidence, `r` banded by `DetectorSet.TrustBand` (0.70 and 0.75) | **shows** the judge and the maintainer the readings; a judge's verdict rests on them | yes: the bands, from keydetect-harness report 07 on 1.2.1 | the evidence is re-read one song per idle tick, all three detectors, and marked *read after this verdict* (ADR 0066); no re-judge |
| the keydetect panel on the dashboard, `KeyDetectorCounts` | keydetect's key against each verdict | **shows** agreement counts | no | follows the re-read evidence |
| chord lane in playback, `ChordStrip`, `KeyLane` | `chords`, `chord_starts`, `chord_timeline` | **shows** | no | follows the re-read evidence |
| key lifts, `KeyLifts` (ADR 0100) | `chroma0` and `chroma1` of `chroma=1`, a full-track scan | **shows** a lift as support for a section in another key (`SectionSupports`) | yes: the fit floor 0.081, on 1.6.0's chroma | **kept**: scanned only where the evidence holds none (gap, [#210](https://github.com/Bamo16/crateside/issues/210)) |
| detune, `TuningPass`, `TuningStore` (ADR 0101) | `tuning_cents`, read over stretches of each instrumental | **decides** a detune written into saved default clips | no, but the readings are the build's | **kept**: a track is decided once and its entry names no build (gap, [#209](https://github.com/Bamo16/crateside/issues/209)) |
| starter warps and `beats.json`, `StarterGrids`, `BeatReader` (ADR 0048, 0065) | beatdetect's grid, BPM and drift (`DriftsAt` 0.1 over 30 s windows) | **shows** a starter grid and a drift flag; a saved warp is never changed (ADR 0107) | the drift threshold, on a beatdetect of 1.4.0 or before | a reading from an older build is read again, a tick at a time (`IsFromOlderBuild`) |
| tempo, `TrackTempo` (ADR 0049, 0050) | beatdetect's BPM against the warp's | **shows** which BPM to trust (`VouchedSpread` 0.02, `ReadingDisagreesAt` 0.01) | no | follows the beat reading |
| re-read progress | nothing | the dashboard does not say how many readings are behind a pin | | gap, [#211](https://github.com/Bamo16/crateside/issues/211) |

The harnesses, [keydetect-harness](https://github.com/Bamo16/keydetect-harness) and
[beatdetect-harness](https://github.com/Bamo16/beatdetect-harness), score each build in numbered
reports under `docs/`; FiLL's contract changelog for keydetect is `docs/contract-changelog.md` in
`ffmpeg-keydetect`.

## Decision

A new build of either filter is adopted by the four gates below. All are mechanical, and **when all
four pass the coordinator pins the build without asking the maintainer**. A failed gate holds the
build, and the maintainer is told which.

### The gates

1. **The harness has a report on the build, with no significant net regression on its yardsticks.**
   Fixed at least equals broken, or the breaks are not significant at p < 0.05.
2. **The output contract holds.**
   - crateside's parsers read the build's output: the parser tests are run against stderr captured
     from the new build.
   - Every entry marked "Changed:" in the filter's changelog since the pinned version is checked
     against the consumer map above, and each consumer it touches is named in the note.
3. **Every consumer calibrated on build output is checked.** Where the output it reads is identical
   on a sample of library tracks, nothing is to do. Otherwise its check is run again. For
   rules-first that is `scripts/keydetect_rules_check.py`, which prints the rules table of two
   builds side by side. **Each rule's precision may fall by at most 1.5 points, and none may fall
   below 95%.** Otherwise the build is not pinned, or the threshold moves in the same change, backed
   by the measurement.
4. **A frozen note in `docs/notes` records the gates' numbers.** Then `BuiltInBuilds` is bumped (the
   release's `SHA256SUMS.txt` and the executable's own sha256) and the maintainer's pin in
   `builds.json` is moved.

### The migration, by kind of consumer

- **Display outputs** (chord lane, chroma and key lifts, beat display, starter grids) follow the pin.
  Stored readings are read again in idle ticks, as ADR 0066 does.
- **Decisions made by rules** (rules-first verdicts, detune) are decided again on the new evidence
  automatically, **including rewriting a key tag on an existing sample, with no confirmation**. The
  maintainer said so on 2026-11-19: "as a crateside user who lets crateside make all the calls
  anyway, I'd be 100% fine with crateside changing a tagged key on an existing sample without
  needing any confirmation from me." An Activity note records each change. There is no setting to
  opt out for now: there are no other users yet.
- **Judge-decided verdicts are never judged again** (cost). A moved reading shows beside the
  verdict, as ADR 0066 does.
- **The maintainer's own picks, saved warps and anything he set by hand never change.**
- **Re-reads run one per idle tick and show on the dashboard.** No throttle setting for now; the
  maintainer may want one later if it drags the machine.

## Where today's behavior differs

Each is filed, and none is built by this ADR.

- Rule-decided verdicts are not decided again on new evidence ([#207](https://github.com/Bamo16/crateside/issues/207), a rule-decided verdict is decided again when its evidence is re-read on a new detector build).
- A key tag already on a sample is never rewritten ([#208](https://github.com/Bamo16/crateside/issues/208), a key tag is rewritten when a re-decided verdict changes the key).
- Detune is decided once and names no build ([#209](https://github.com/Bamo16/crateside/issues/209), detune decided again when the keydetect pin changes).
- Key lifts are not scanned again on a new build ([#210](https://github.com/Bamo16/crateside/issues/210), key lifts scanned again when the pin changes).
- No re-read progress on the dashboard ([#211](https://github.com/Bamo16/crateside/issues/211), how many readings are behind the pinned builds).

Until they are built, gate 3 is the only thing between a build and a stale decision, so it is run
even where the output looks unchanged.

## First application: keydetect 1.6.0

- Gate 1: keydetect-harness report 10, *keydetect 1.6.0: the minor profile*: ledger 163 to 169 of
  222 right (10 fixed, 4 broken, p = 0.18), corpus 51 to 50 of 63 (0 fixed, 1 broken, p = 1.0).
- Gate 2: the changelog's "Changed:" entries since 1.3.0 are all about the answer (1.5.0 on real
  recordings, 1.6.0 on minor-key recordings), none about an output line's form. They touch the key
  and `r` (rules, evidence) and the chords (chord lane).
- Gate 3: `tuning_cents` and `chroma=1` are identical between 1.3.0 and 1.6.0, so detune and the key
  lifts do not move. Rules-first: [the note of 2026-11-19](../notes/2026-11-19-rules-first-on-keydetect-1.6.0.md).
  The largest fall is 0.6 point (96.8% to 96.2%), none is below 95%, and tier B's `r` cutoff stays at 0.80.
