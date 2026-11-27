# crateside: bring a detuned song back to A = 440 with the clip's detune

Issue: #187 (bring a song tuned away from A = 440 back to it, with the clip's detune)

For an implementing agent; Bamo reviews first. Terms (sample, stem, clip gain) are in `CONTEXT.md`;
the sidecar rewrite this copies is [ADR 0059](../../adr/0059-a-sample-arrives-at-one-loudness.md).

## Problem

Bamo, in the issue: "Some songs aren't tuned to A = 440 Hz, and in a mashup that shows: *Just a
Friend* sits audibly sharp against everything else. Like clip gain, this can be corrected without
touching the audio: an `.asd` clip has `PitchFine` (detune, in cents, ±50) next to `PitchCoarse`."
Measured 2026-11-06 on 60 sources: 41 within 5 cents of 440, 12 between 10 and 35 off (*Just a
Friend* +17.5, *Persona* +34.5, *iluvslapbass* −35.5). The reading is unstable on tracks with
little pitched material (*Shimmy Shimmy Ya* −45 then +11, *Nissan Altima* +9 then −38).

## Current behavior

- `keydetect` already prints `lavfi.keydetect.tuning_cents`; crateside never parses it.
  `KeydetectFilter.Parse` (`src/Crateside.Core/KeyDetection/KeydetectFilter.cs:99`) reads the key,
  `key_confidence` (regex at :178), chords and key events, and `KeydetectReading.Read` (:211) has no
  tuning field.
- A reading is saved into the ledger evidence by `DetectorSet`
  (`KeyDetection/DetectorSet.cs:126-144`) as `MachineDto` / `FfmpegDto`
  (`Ledger/RunEvidence.cs:658, 726`). Per-window readings are in `MachineDto.WindowDto`
  (`RunEvidence.cs:281`), so a track already carries several stretches.
- Clip gain is the model for a sidecar write: `SidecarWriter.Level`
  (`src/Crateside.Core/Warps/SidecarWriter.cs:340`) goes through `Rewrite`/`RewriteFile` (:382-402),
  which only touches a sidecar whose default clip is saved, and is driven by `Leveling.Level`
  (`Loudness/Leveling.cs`) with a note recorded through `StageNotes` ("Loudness").
- AbleKit already reads and writes `PitchFine` (`AnalysisFileParser.cs:80`). Nothing in crateside
  sets it.

## Design

### Gate before any code

**Step 0, by Bamo in Live (not the agent):** put *Just a Friend* in a set with the clip's detune at
−17 cents in each warp mode the library uses (Beats, Tones, Texture, Complex, Complex Pro,
Re-Pitch) and confirm the detune is heard. Live's Re-Pitch probably has no detune control, so the
field is likely ignored there; the agent skips a Re-Pitch clip.
**Confirm:** wait for step 0 before implementing. Alternative: build now and gate by warp mode
afterwards.

### 1. Keep the reading

- Parse `lavfi.keydetect.tuning_cents` in `KeydetectFilter` (a named-group `[GeneratedRegex]`, last
  match wins like the others). `KeydetectReading.Read` gains `double? TuningCents = null`; null
  when the build prints none.
- `MachineDto.WindowDto` and the whole-run `FfmpegDto` keep it as `TuningCents`. Old evidence has
  no value and reads as null.
- Track details shows it in the **Key detectors** evidence rows as `tuning +17.5 ¢` beside the
  confidence, with the spread when there are several readings (`+17.5 ¢ · 3 stretches, spread 2`).

### 2. A steady reading decides

The tuning is **one value per track**, applied to every sample (source, instrumental, vocal, drums)
so stems of one song never detune against each other.

- Readings come from the **instrumental stem**'s windows if the track has one, else the source:
  the stem is what gets mixed, and vocals are the noise. **Confirm:** instrumental first, source as
  fallback. Alternative: always the source, so a value exists before separation.
- **Steady** = at least 3 readings and max − min ≤ 6 cents; the value is their median. Not steady:
  nothing is written and the track goes to the review list. **Confirm:** 6 cents (the issue found
  39 of 59 agreeing within 3 across two stretches). Alternative: 10.

### 3. Write it, undoably

- **Threshold: |median| ≥ 10 cents.** Below that nothing is written (41 of 60 sources are inside
  5). **Confirm:** 10. Alternative: 8, which catches more of the quiet cases.
- `SidecarWriter.Detune(audioPath, cents)` beside `Level` (:340), same `Rewrite` guard: only a
  saved default clip, and only when `PitchFine` is 0 or was written by crateside. A `PitchFine`
  Bamo set stays his. The value written is `−median`, clamped to ±50.
- **Which way at the edge.** `tuning_cents` is measured against the nearest semitone, so it lies
  within ±50 and `−tuning` is always the smallest move to a grid-aligned pitch. *Persona* (+34.5)
  would be corrected by −34.5, not by +15.5 up to the next semitone, and the key tag is untouched
  because the move is under a semitone. **Confirm:** always −tuning. Alternative: choose the
  direction by the key verdict.
- **Between 30 and 50 cents** the song sits nearly halfway between two semitones and the key
  verdict is itself the weak part (#171, a second key centre needs independent support). Such a
  track is **not written automatically**; it goes to the review list with its median.
  **Confirm:** review list above 30. Alternative: write everything at 10 or more.
- Record it in the Activity notes (`StageNotes.Record(trackKey, "Detune", …)`) as
  `Detuned −17 ¢ (readings +16, +18, +17.5)`, and keep the previous `PitchFine` in the ledger
  beside the evidence so it can be undone.
- **Undo**: a *Put back* button on the Track details Key detectors row restores the saved previous
  value and marks the track "left alone" so the pass does not write it again.

### 4. Where it runs

A new `Tuning` domain folder in `Crateside.Core` with a `TuningPass` shaped like `Leveling`; the
`Detune` write stays in `SidecarWriter` with the other clip writes. It runs after the evidence run
for a track, and over the library from `DetectorBackfill`
(`Orchestration/Evidence/DetectorBackfill.cs`) in idle windows, re-reading keydetect only where the
stored evidence has no tuning. A detector reading is not a judging, so the no-re-judging rule holds.

### The review list

One table, one row per track not written (unsteady, or 30 to 50 cents): song, median, each
stretch's reading, and **a blank answer cell**. Bamo fills it in; only what he accepts is written.

## Test plan

- `KeydetectFilterTests`: stderr with `tuning_cents=17.5` parses; without it, null.
- `SidecarWriterTests`: `Detune` writes `PitchFine = −17.5` on a saved default clip; leaves a clip
  whose `PitchFine` Bamo set; leaves an unsaved clip; clamps at ±50.
- Pass tests: 3 readings within 6 → written; a spread of 9 → review; median 9.9 → not written;
  median 34.5 → review; undo restores the previous value.
- On library tracks, in Track details and then Live's clip view of the track's lanes in the
  warp view:
  - *Just a Friend*: tuning about +17.5, written −17.5; it sits in tune against a clean 440 track.
  - *In the Navy* and *I Want You Back*: both old records, tuning read and written if steady.
  - *Persona* (+34.5) and *iluvslapbass* (−35.5): on the review list, nothing written.
  - *Shimmy Shimmy Ya* and *Nissan Altima*: unsteady, review list, nothing written.
  - Any track from the 41 within 5 cents: its sidecar is not rewritten.

## Out of scope

Re-judging any track; changing the key tag or verdict for tuning; detune for Re-Pitch clips;
re-tuning the audio itself; the second-key-centre rule (#171).
