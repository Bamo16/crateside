# Rules first on keydetect 1.6.0

*Note, 2026-11-19, frozen. Asks whether moving the keydetect pin from 1.3.0 to 1.6.0 changes how
precise the detector-only rules of [ADR 0076](../adr/0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md)
are, the ones measured in [the note of 2026-11-03](2026-11-03-what-rules-can-rule-on-without-a-judge.md).
Decides nothing. Nothing in crateside was changed, `builds.json` still pins 1.3.0, no track was
judged, and madmom and Essentia `edma` were not re-run.*

## The question

1.6.0's default `minor_profile=edma` takes the minor row of its key profile from EDMA, and Essentia's
`edma` is one of crateside's two backers (the other is madmom). If keydetect now leans on the same
profile as a backer, "keydetect and that backer agree" may be worth less. Do the rules still hold?

## What was measured

- **Songs (MEASURED):** the latest run of each song with a HIGH or MEDIUM verdict decided by the
  judge and readings from all three detectors: 592 in the ledger today. 570 have their source audio
  on disk (the run's `InputPath`, else the source index); the other 22 are left out of **both**
  columns. The 32 runs decided by `Rules` are excluded, since a rule's verdict is the detectors'
  own.
- **Readings:** madmom and `edma` as stored in the ledger. keydetect 1.3.0 as stored (the pinned
  build, sha256 `28f445ee…`); keydetect 1.6.0 freshly read from `ffmpeg-keydetect-1.6.0.exe` as
  crateside reads it: the whole track, `-nostdin -hide_banner -nostats -i <file> -vn -af keydetect
  -f null -`, default options, no window, the file given directly (the 1.6.0 build cannot pipe).
  The key and `key_confidence` (`r`) come from the final summary lines.
- **Check on the join (MEASURED):** run on the 582 songs of the 2026-11-03 note (runs before
  2026-11-04), the rules give that note's table to the song: 319 / 315, 217 / 214, 448 / 417,
  262 / 253, 540 / 484 and 92 / 67. A rerun of 1.3.0 on 23 of today's songs, spread across the
  ledger, gave the stored key and `r` (within rounding) on all 23, so the source files the audio is
  read from now are the ones the ledger's readings came from.
- **Reference:** the judge's verdict collapsed to major or minor, as before. Not ground truth, and
  INFERRED to favour 1.3.0 a little: for part of these songs the judge saw 1.3.0's reading and 1.6.0's
  was never in front of it. The `read_after_verdict` slice of the earlier note no longer separates
  anything: crateside re-read every run's evidence on 2026-11-17, so all 570 carry the flag.
- **Rules** are the 2026-11-03 note's, and "agree" is the same key (tonic and major or minor).

## The detectors alone (MEASURED)

570 songs, the same songs in both columns. Fires on is a count with its share of 570; agrees is of
the fires.

| rule | 1.3.0 fires | 1.3.0 agrees | 1.6.0 fires | 1.6.0 agrees |
|---|---|---|---|---|
| all three agree | 305 (54%) | 302 (99.0%) | 322 (56%) | 318 (98.8%) |
| all three, keydetect `r` ≥ 0.80 | 206 (36%) | 204 (99.0%) | 253 (44%) | 251 (99.2%) |
| keydetect and one backer | 435 (76%) | 406 (93.3%) | 455 (80%) | 423 (93.0%) |
| keydetect and one backer, `r` ≥ 0.80 | 252 (44%) | 244 (96.8%) | 318 (56%) | 306 (96.2%) |
| any two agree | 526 (92%) | 471 (89.5%) | 529 (93%) | 472 (89.2%) |
| madmom and Essentia against keydetect | 91 (16%) | 65 (71.4%) | 74 (13%) | 49 (66.2%) |

What the misses are:

| rule | 1.3.0 | 1.6.0 |
|---|---|---|
| all three agree | 2 relative, 1 fifth | 1 parallel, 2 fifth, 1 relative |
| keydetect and one backer, `r` ≥ 0.80 | 4 parallel, 3 relative, 1 fifth | 3 parallel, 5 relative, 3 fifth, 1 other |

- **Precision does not move beyond a song or two.** The `r` ≥ 0.80 rule goes 8 misses to 12 while
  it fires 66 more times (252 to 318): 62 more right and 4 more wrong, so 94% on the added
  songs. The precision difference is 0.6 points, which 570 songs cannot tell from nothing (INFERRED).
- **`r` runs higher on 1.6.0**, median 0.798 to 0.819, so a fixed 0.80 admits more songs. That is
  where the extra fires come from, not a change in who agrees.
- **keydetect itself is a little better against the verdict**: 446 right on 1.3.0 and 460 on 1.6.0.
  It changed its key on 77 songs: 42 went from wrong to right, 28 from right to wrong, 7 stayed
  wrong. Minor verdicts gained (276 to 299 right), major ones lost (170 to 161).

## Is keydetect still independent of `edma`? (MEASURED)

Agreement of keydetect with each backer, split by the verdict's mode.

| verdict | songs | keydetect = edma 1.3.0 | 1.6.0 | keydetect = madmom 1.3.0 | 1.6.0 |
|---|---|---|---|---|---|
| major | 205 | 149 (73%) | 145 (71%) | 143 (70%) | 143 (70%) |
| minor | 365 | 231 (63%) | 248 (68%) | 217 (59%) | 241 (66%) |
| all | 570 | 380 (67%) | 393 (69%) | 360 (63%) | 384 (67%) |

- **On minor songs keydetect agrees more with both backers**, edma by 5 points and madmom by 7. If
  borrowing `edma`'s minor row were making it a copy of `edma`, the edma column alone would rise;
  madmom's rose more (INFERRED: the new minor row makes keydetect more correct on minor songs, so it
  meets every backer that is correct there more often).
- **Shared errors, the real test.** When keydetect is wrong, how often does a backer name the same
  wrong key?

| | keydetect wrong | `edma` shares it | madmom shares it | both |
|---|---|---|---|---|
| 1.3.0 | 124 | 22 (18%) | 10 (8%) | 3 |
| 1.6.0 | 110 | 21 (19%) | 15 (14%) | 4 |

  `edma` already shared keydetect's errors more than madmom on 1.3.0, and does not share more of
  them on 1.6.0. It is madmom's share that grew, from 10 to 15.
- **By backer, rule `r` ≥ 0.80:** keydetect and `edma`, 232 of 240 (96.7%) on 1.3.0 and 283 of 292
  (96.9%) on 1.6.0; keydetect and madmom, 216 of 218 (99.1%) and 274 of 279 (98.2%). A song only
  `edma` backs (madmom disagrees) is right 56 of 75 on 1.3.0, 54 of 71 on 1.6.0; only madmom,
  48 of 55 and 51 of 62.
- The `r` ≥ 0.80 rule has 7 new misses and loses 3 (*Fantasy*, *So Fresh, So Clean*, *Superstition*).
  Of the 7, three are backed only by `edma` (*Entwined*, *Waterfall*, *As It Was*), three only by
  madmom (*Blue Shift*, *From Zero*, *Love Me Again*) and one by both (*Hate It or Love It*).

## Songs whose outcome flips (MEASURED)

Only flips that involve a miss: a rule that fired wrongly on one build and was silent or right on
the other. kd is keydetect's key and `r`; backers are madmom (mm) and `edma` (ed).

New misses on 1.6.0:

| song | verdict | keydetect 1.3.0 | keydetect 1.6.0 | backers | rule that now fires wrong |
|---|---|---|---|---|---|
| Knower - The Government Knows | G | G 0.748 | Gm 0.796 | mm Gm, ed Gm | all three agree |
| The Game, 50 Cent - Hate It or Love It | C#m | C#m 0.758 | G#m 0.852 | mm G#m, ed G#m | all three agree, and both `r` ≥ 0.80 rules |
| Three 6 Mafia, UNK - I'd Rather | Bm | Bm 0.652 | F#m 0.695 | mm F#m, ed F#m | all three agree |
| Sub Focus, Grimes - Entwined | G#m | G#m 0.616 | D#m 0.821 | mm G#m, ed D#m | keydetect + one backer (was right) |
| LeMaitre - Blue Shift | G#m | G#m 0.806 | B 0.805 | mm B, ed G#m | keydetect + one backer (was right) |
| Disclosure, Raye - Waterfall | D | Em 0.689 | Bm 0.838 | mm D, ed Bm | keydetect + one backer, and `r` ≥ 0.80 |
| Neko Hacker, Rika - From Zero | Gm | Gm 0.747 | A# 0.862 | mm A#, ed Gm | keydetect + one backer, and `r` ≥ 0.80 |
| Harry Styles - As It Was | A | Bm 0.791 | Bm 0.815 | mm A, ed Bm | `r` ≥ 0.80 only (r rose past it) |
| John Newman - Love Me Again | Gm | Gm 0.81 | Dm 0.805 | mm Dm, ed Gm | keydetect + one backer (was right) |
| Polo & Pan - Arc-en-Ciel | G | G 0.832 | Gm 0.798 | mm Gm, ed G | keydetect + one backer (was right) |
| Beyoncé, Jay-Z - Crazy in Love | Gm | Gm 0.812 | A# 0.755 | mm A#, ed Gm | keydetect + one backer (was right) |
| Beck - Black Tambourine | G# | G# 0.762 | D#m 0.783 | mm D#m, ed D# | keydetect + one backer (was silent) |
| The Weeknd - Blinding Lights | Cm | Cm 0.682 | Fm 0.753 | mm D#, ed Fm | keydetect + one backer (was silent) |
| Mick Gordon - The Only Thing They Fear Is You | D# | G#m 0.411 | D#m 0.577 | mm D#m, ed D# | keydetect + one backer (was silent) |

Misses that 1.6.0 removes or turns right:

| song | verdict | keydetect 1.3.0 | keydetect 1.6.0 | what changes |
|---|---|---|---|---|
| Real McCoy - Run Away | A#m | C# 0.764 | A#m 0.711 | all three agree: wrong, now silent |
| OutKast - So Fresh, So Clean | Cm | Fm 0.873 | Cm 0.795 | all three agree and both `r` ≥ 0.80 rules: wrong, now silent |
| Mariah Carey - Fantasy | G | Em 0.827 | Em 0.782 | `r` ≥ 0.80: wrong, now below 0.80 |
| Stevie Wonder - Superstition | D#m | D# 0.814 | D#m 0.840 | keydetect + one backer, and `r` ≥ 0.80: wrong, now right |
| 50 Cent - Many Men | Bm | Em 0.72 | Bm 0.893 | keydetect + one backer: wrong, now right |
| Phantoms - Need You Closer | G#m | B 0.745 | G#m 0.827 | keydetect + one backer: wrong, now right |
| Group X - Arabian Rap Sensations | Gm | D# 0.696 | Gm 0.653 | keydetect + one backer: wrong, now right |
| Riff Raff - Dolce & Gabbana | A#m | A# 0.656 | A#m 0.676 | keydetect + one backer: wrong, now right |
| State of Mind, PNC - City on Fire | G#m | G# 0.722 | G#m 0.865 | keydetect + one backer: wrong, now right |
| Moe Shop - Wonder Pop | Cm | D# 0.745 | Cm 0.731 | keydetect + one backer: wrong, now right |
| Awich - Queendom | D | Em 0.683 | Bm 0.685 | keydetect + one backer: wrong, now silent |

Still wrong on both builds under the `r` ≥ 0.80 rule: *How Low*, *Party in the U.S.A.*, *Dragula*,
*Zero*, and *Faded* (all three detectors). The all-three-agree rule drops *Run Away* and *So Fresh,
So Clean* and gains *The Government Knows*, *Hate It or Love It* and *I'd Rather*; in each of those
three keydetect moved onto the key the two backers already had.

## What this does not show

- 22 of the 592 songs had no source on disk, and the verdicts are the judge's, partly after seeing
  1.3.0. A smaller edge for 1.3.0 than measured is as likely as a larger one (INFERRED).
- The tier A and tier B rules also read pages; this note measures the detector-only rules, so tier B's
  miss rate on 1.6.0 is not stated here beyond the rules inside it.
- 570 songs cannot separate a 0.6 point change in precision.

## Recommendation

Pin 1.6.0, and move no threshold: precision holds (all three agree 99.0% to 98.8%, keydetect and one
backer at `r` ≥ 0.80 96.8% to 96.2%), keydetect is right on 14 more songs, and `edma` shows no sign of
having become a copy of it. Because `r` runs higher on 1.6.0, tier B's 0.80 now admits 318 songs
instead of 252; raising it to 0.83 would hold coverage near the old count (241 fires, 237 right,
98.3%) if that is wanted.
