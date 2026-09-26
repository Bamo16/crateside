# The `.asd` sidecar, field by field

*Note, 2026-09-26, frozen. For [#71](https://github.com/Bamo16/crateside/issues/71). Corrects
[what the sidecar records](2026-09-26-what-the-sidecar-records.md) on the four fields after the
warp markers.*

Every sidecar under Mashup Samples and Sample Staging (847, of which 845 in the library) was read
end to end by a throwaway parser, and every one ended exactly at end of file. Two Live 12
sidecars from [DBraun/AbletonParsing](https://github.com/DBraun/AbletonParsing)'s tests, identical
but for the clip's Loop switch, parse the same way. That repo parses Live 9 and 10; only its
descriptions of the clip fields were used, checked against its Live 12 pair.

Each meaning below is labelled **measured** (established on real files here), **owned** (the user
said so) or **guessed**.

## Layout

| part | bytes | what |
|---|---|---|
| head | `06 49`, int32 *n*, *n* int32 | a table of sample positions from 0 to the audio's sample count |
| | 20 bytes | `0, 0, 100, 4, 0` as int32, the same in every file |
| | 1 byte | **default clip saved**: 0 before *Save Default Clip*, 1 after |
| chunk | `ab 1e 56 78`, byte 5, int32 365 | framing; then the root name `SampleData` and its type schema |
| | | the `SampleData` instance |
| trailer chunk | `ab 1e 56 78`, byte 5, int32 0 | an `AufTaktData` schema and instance, identical to the inline one in all 845 |

**Encoding, measured.** A class name is a big-endian u16 length and ASCII. Field type codes:
`0x10` bool (1 byte), `0x11` int32, `0x12` float32, `0x17` float64, `0x00` a nested class; four
array codes, each an int32 count then the elements: `0x31` bytes, `0x32` 2-byte values, `0x35`
int32, `0x40` float32. Types declared with field count −1 are variable-length:

- **`RemoteableList`, `List<T>`:** int32 next id, then per element its class name, an int32 id and
  its fields, then **an empty name (`00 00`) ending the list**. The earlier note missed this
  terminator and read `MarkersGenerated`, `LaunchMode`, `LoopOn` and `LaunchQuantisation` 2 bytes
  early. Its "`LaunchQuantisation` 256 → 0" at *Save Default Clip* was **`LoopOn` true → false**.
- **`RemoteableArray`:** int32 count, the element class name (written even when the count is 0),
  then the elements packed, without ids or names.

Little-endian throughout except the name lengths.

## Where the bytes go

On the Disconnect instrumental (509,038 bytes, 164.9 s): the overview's finest level is 455 KB,
the head table 30 KB, the transients 18 KB. The clip settings and markers are under 300 bytes.

## Fields

Spread is over the 845 library sidecars unless stated.

### Clip

| field | type | meaning | spread |
|---|---|---|---|
| `LoopStart` | double, beats | with Loop off, the clip's **start marker**; with Loop on, the loop start. *Measured* on the DBraun pair; the user sets it to verse 1 (*owned*, earlier note) | 58 values; 821 on a bar line; 32: 162, 64: 90, 16: 73, 48: 59, 0: 53 |
| `LoopEnd` | double, beats | with Loop off, the **end marker**; with Loop on, the loop end. *Measured* as above | within 1 beat of the audio's end on 493 of 844; 0 only on the unsaved file |
| `SampleOffset` | double, beats | with Loop on, start marker = `LoopStart` + `SampleOffset` (−4 on the DBraun pair). *Measured* | 0 on all |
| `HiddenLoopStart`, `HiddenLoopEnd` | double, beats | the loop brace, remembered while Loop is off. *Measured* on the DBraun pair | start 0 on 796, negative on 22 |
| `OutMarker` | double, beats | the end marker, in both Loop states on the DBraun pair (*measured*). In the library it often disagrees with `LoopEnd`: *guessed* to be set when the clip was first placed and left alone by later warps while Loop stays off | equals `LoopEnd` on 601; differs on 244, all Loop off; on 176 of those, `OutMarker` ÷ duration is a whole BPM (120: 21) |
| `Sync` | bool | *guessed*: follows the set's tempo | true on all |
| `HiQ` | bool | *guessed*: high-quality interpolation | true on 844; false on the unsaved file |
| `Fade` | bool | *guessed*: clip edge fades | false on all (true on the DBraun pair) |
| `IsWarped` | bool | the Warp switch; true on a sidecar nobody warped (earlier note) | true on all |
| `SampleVolume` | float, linear | clip gain; *measured* as whole decibels | 1.0 on 841; −8, +1, −2, −1 dB on one each |
| `VelocityAmount`, `PitchCoarse`, `PitchFine` | float | *guessed* by name | 0 on all |
| `ColorIndex` | int32 | clip colour; −1 before the first save (*measured*) | 16 values; stems of one song share a colour in 10 of 405 songs |
| `LaunchMode`, `LaunchQuantisation` | int32 enum | *guessed* by name | 0 on all (`LaunchQuantisation` 1 on the DBraun pair) |
| `LoopOn` | bool | the Loop switch. *Measured* on the DBraun pair, the only field that differs between them | false on 844; true on the unsaved file, and on the Disconnect stem before its save |

### Warp

| field | type | meaning | spread |
|---|---|---|---|
| `WarpMode` | int32 enum | crateside's `WarpMode` order | Complex (4) on 844; Complex Pro (6) on *ReeK, Asatsumei, L4hee – Deity Mode (Instrumental)* |
| `TransientResolution`, `TransientLoopMode`, `TransientEnvelope`, `GranularityTones`, `GranularityTexture`, `FluctuationTexture`, `ComplexProFormants`, `ComplexProEnvelope` | int32, float | per-mode parameters; *guessed* to be Ableton's defaults | 6, 2, 100, 30, 65, 25, 100, 128 on all |
| `TimeSignature` | 2 floats, double | numerator, denominator, and `Time`, *guessed* to be where it applies | 4/4 at 0 on all |
| `WarpMarkers` | list of `WarpMarker` (id, `SecTime`, `BeatTime`) | markers, with ids from the list's allocator | 2 on 712 (one user marker and Ableton's handle 1/32 beat on); 21+ on 106 |
| `MarkersGenerated` | bool | *guessed*: the markers came from auto-warp | true on 3, each with 2 markers: *Carter Vail – Dirt Man* (both stems), *Snail's House – love birds* |

### Transients

| field | type | meaning | spread |
|---|---|---|---|
| `OnSets.Positions` | int32 array, samples | **Ableton's detected transients**. *Owned*: the user places drum-stem markers on them. *Measured*: different per stem (Disconnect: 704 instrumental, 462 vocal, none shared) | median 244 per minute |
| `OnSets.TransitionEnergies` | float32 array | one strength per transient, max 1.0 | |
| `OnSets.IsSet`, `OnSets.Version` | bool, int32 | | true, 5 on all (4 on the DBraun pair) |
| `UserOnsets` | array of `OnsetEvent` (`Time` s, `Energy`, `IsVolatile`) | the transients as the saved clip holds them; empty until *Save Default Clip* (*measured*) | see below |
| `UserOnsets.HasUserOnsets` | bool | | true on 819, false on 26 |

**Volatile onsets are the transient Ableton adds under a warp marker** (*measured*): 15,469 of the
library's 15,474 sit exactly on a marker's `SecTime`, and all have energy `double.MaxValue`. On the
Disconnect stem the user placed the marker on a detected transient and then nudged it (*owned*).
The marker ended 101 samples before the transient. The saved clip kept the transient where it was
and added a volatile one at the marker.

Of the 819 saved sets, 686 are exactly the detected transients plus the volatile ones. 98 hold
only the volatile ones and none of the detected. In 33, one to three detected transients moved by
27 to 698 samples; *guessed* to be transients the user dragged. 2 differ more widely.

### Tempo analysis

| field | type | meaning | spread |
|---|---|---|---|
| `AufTaktData.PreprocessedDataChunk` | byte array | *guessed*: Ableton's beat analysis ("Auftakt" is German for an upbeat) | empty on 831; 161–444 KB on 14 |
| `AufTaktData.UnbiasedTempoEstimate` | double, BPM | Ableton's tempo estimate; `double.MaxValue` when unset | set on the same 14 |
| `AufTaktData.IsSet`, `.Version` | bool, int32 | | false and `int.MinValue` on 831; true and 5 on 14 |

The 14 are 8 songs, both stems for 6 of them. Against the warped tempo, the estimate was within 0.5 BPM on
6, within 1 on 3 more, half or double on 2, and wrong on 3 (100.7 and 95.1 against 70; 68.9 against
124). Nothing here says what makes Ableton write it.

### File

| field | type | meaning | spread |
|---|---|---|---|
| `ExtraLength` | int32 | | 0 on all |
| `OriginalFileSize` | int32, bytes | the audio's size when analysed; exact on the Disconnect stem (*measured*) | matches on 828. Of the other 17, 15 hold the same number of samples as recorded. The *1991 – Jungle (Colin Hennerz Remix)* pair is 645 samples longer now than when Ableton analysed it, and was not re-analysed |
| `OverView.OverViewLevels` | list of float16 arrays | **the waveform**: min and max per channel per bin, interleaved. *Measured* against decoded audio, within 0.0005 at every level | 4 levels; 3 on two 36-second files |
| `OverView.SamplesPerBinLog2` | int32 | level *k*'s bin is 2^(this × (*k*+1)) samples | 7 up to 379 s; 8 from 387 s (13 files) |
| `OverView.ChannelCount`, `.Version` | int32 | | 2, 2 on all |

The head's position table (*n* entries, median 942 samples apart, ending on the sample count) is
**not decoded**. It is not the transients (6 of 704 in common on the Disconnect stem), and a WAV's
sidecar has one too, so it is not a FLAC seek table.

## What follows

- **For #67:** the head's saved flag is the cheapest "default clip saved" signal, reached without
  the schema. It agrees with markers present on all 847 sidecars and on both snapshots. The start
  marker is `LoopStart` while Loop is off, which is every saved file in the library.
- **For #68:** `UnbiasedTempoEstimate` exists on 14 files of 845 and is not a tempo source. Tempo
  from markers stands.
- **For #54:** markers are positions in the audio in seconds; the volatile transients follow from
  them, so a re-cut needs only the markers. A re-cut that changes the audio's length (as *Jungle*'s
  did) moves every later position.
- `OutMarker` should not be read as the clip's end; `LoopEnd` is.
