# The detectors and sources, scored across the ledger

*Note, 2026-10-15, frozen. For [#108](https://github.com/Bamo16/crateside/issues/108) (keytag's
future: crateside owns a key-only judge) and cited by
[ADR 0055](../adr/0055-crateside-owns-the-key-judge-and-its-ledger.md); decides nothing itself.
Read from keytag's `results/runs.jsonl` at `6517c91`, latest run per song, songs with a machine
layer only: 519 songs. No audio was run and nothing was written.*

## What "right" was scored against

Two yardsticks, neither independent of the detectors:

- **Confident verdict**: HIGH or MEDIUM, 495 songs. Circular, because the judge weighed these
  same three detectors when it reached the verdict.
- **Human source**: an `authoritative` or `corroborating` source (Hooktheory, Musicnotes, a tab
  site) stated a key, and every such source on the song stated the same one, 227 songs. Less
  circular: the key is a person's, though the judge chose which sources to record.

"Right" means the same tonic and quality as the yardstick. A mode recorded beside the key is
ignored.

## Each detector alone

| | vs. verdict | vs. human source | the only one of three right (verdict / human) |
|---|---|---|---|
| ffmpeg `keydetect` | 74% | 70% | 55 / 28 |
| CREMA | 64% | 60% | 30 / 16 |
| librosa K-S | 58% | 56% | 25 / 15 |

keydetect's misses against verdicts are fifth 8%, other 6%, relative 6%, parallel 4%.

## The other two as a signal of doubt

keydetect's accuracy, split by its own `r` and by how many of the other two name the same key:

| keydetect's answer | backed by both | by one | by neither |
|---|---|---|---|
| r ≥ 0.80, vs. verdict | 113/118 (95%) | 51/60 (85%) | 18/32 (56%) |
| r < 0.80, vs. verdict | 67/72 (93%) | 81/103 (78%) | 37/110 (33%) |
| r ≥ 0.80, vs. human | 55/60 (91%) | 20/26 (76%) | 8/14 (57%) |
| r < 0.80, vs. human | 25/29 (86%) | 31/44 (70%) | 20/54 (37%) |

Agreement from the other two predicts keydetect being right far better than `r` does. As a single
partner the two are close: keydetect is right 85% of the time when CREMA agrees and 84% when
librosa agrees (human yardstick), and about 50% when the partner disagrees, in either case.

When keydetect is wrong, CREMA and librosa agreeing on the right key happened 32 of 128 times
(verdict) and 12 of 68 (human). They rarely supply the answer; they flag the doubt.

The chord stream and key flicker in the evidence come from keydetect, not CREMA
(`detect_cli.py`, `detail.chords`).

No other detector has been scored on this library. keydetect-harness evaluates only keydetect's
own revisions.

## The sites the judge cited

How often a source's stated key matched the verdict, by site and the kind the judge gave it:

| site | kind | cited | stated key matches verdict |
|---|---|---|---|
| hooktheory.com | authoritative | 173 | 167/171 |
| hooktheory.com | corroborating | 15 | 12/12 |
| musicnotes.com | authoritative | 59 | 54/57 |
| tabs.ultimate-guitar.com | corroborating | 58 | 41/42 |
| tunebat.com | algorithmic | 157 | 53/126 (42%) |
| songbpm.com | algorithmic | 47 | 18/35 (51%) |
| chordify.net | chord recognizer | 30 | 12/17 |

446 of the 519 songs cite at least one source. The algorithmic sites match the verdict less often
than keydetect alone does. The Hooktheory figure is circular in the same way as the verdict
yardstick, since the judge treats Hooktheory as authoritative.

## Where a verdict overruled an authoritative source

Eight songs, six of them recorded HIGH:

| song | verdict | source states |
|---|---|---|
| Rob Zombie – Superbeast | G# minor, MEDIUM | E minor (Musicnotes) |
| Beyoncé – Single Ladies | E minor, HIGH | E major (Hooktheory) |
| fusq – perfume! | C# minor, MEDIUM | F# minor (Hooktheory) |
| Real McCoy – Run Away | C# major, HIGH | A# minor (Hooktheory) |
| Three Days Grace – Animal I Have Become | C minor, HIGH | D minor (Musicnotes) |
| Daft Punk – Get Lucky | B minor, HIGH | F# minor (a blog) |
| BTS – Dynamite | E major, HIGH | C# minor (Musicnotes) |
| Daft Punk – Around the World | E minor, HIGH | A minor (Hooktheory) |

The four Hooktheory pages were read on 2026-10-15:

- **Around the World**: "A Dorian (verse), E minor (chorus)". Not a dissent; the import recorded
  the first section.
- **Single Ladies**: the tab analyzes the melody (E major); the harmony is Aeolian. Same tonic,
  two modes at once.
- **Run Away**: one section, *Chorus*, B♭ minor. The ledger's claim came from a search snippet,
  which was accurate. Ultimate Guitar says D♭ major. A relative tie, settled by the judge from
  chord order alone and recorded HIGH.
- **perfume!**: one section, *Instrumental*, F♯ minor, 127 BPM (the tempo Tunebat and SongBPM
  give, so the same recording). A real dissent; all three detectors say C# minor.

Not checked: whether the Musicnotes dissents are transposed arrangements rather than the original
published key.
