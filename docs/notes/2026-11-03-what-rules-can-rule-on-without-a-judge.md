# What rules can rule on without a judge

*Note, 2026-11-03, frozen. For [#164](https://github.com/Bamo16/crateside/issues/164) (the judge
made optional). Decides nothing itself. Read from the ledger as it stood on 2026-11-03, latest run
per song; no judging was run and nothing was written. The page lookups were run fresh with
crateside's own `SourcePages`.*

## The question

With no agent, how many songs can crateside rule on confidently from the detectors and the pages
it looks up by itself, and how often would that ruling match the judge? The rest wait for the
person using crateside, who is then the judge.

## What was measured

- **Songs:** 622 in the ledger. 582 have a HIGH or MEDIUM verdict and a reading from all three
  current detectors (keydetect, madmom, Essentia `edma`); 11 more are LOW or WITHHELD. The other
  29 lack madmom or Essentia readings and are left out.
- **Readings:** every one is on keydetect 1.3.0, the pinned build. That is what a rule would see
  today, **not necessarily what the judge saw**: 447 runs carry `read_after_verdict`, so their
  readings were filled in after the verdict on an older build (and on the July runs, with no
  madmom or Essentia at all). Those 447 are the least circular slice. The other 135 include the
  re-judges made because a new keydetect build flipped its reading, where the judge did see 1.3.0.
- **Reference:** the judge's verdict, collapsed to major or minor. It is not ground truth. A second
  yardstick: the 254 songs where every human source the judge recorded (authoritative or
  corroborating) states the same key.
- **Pages:** the ledger's `Lookups` column is empty on every run (it began on 2026-11-01 and no
  song has been judged since), so nothing recorded says what a lookup finds. Two stand-ins:
  the pages the judge cited (an upper bound: found and vetted by an agent), and a fresh lookup of
  120 confident songs drawn at random (seed 164) from those not looked up before.

## The detectors alone

Against the 582 HIGH and MEDIUM verdicts:

| rule | fires on | agrees with the verdict | misses |
|---|---|---|---|
| all three agree | 319 (55%) | 315 (98.7%) | 3 relative, 1 fifth |
| all three, keydetect `r` ≥ 0.80 | 217 (37%) | 214 (98.6%) | 2 relative, 1 fifth |
| keydetect and one backer | 448 (77%) | 417 (93%) | 11 parallel, 10 relative, 6 fifth, 4 other |
| keydetect and one backer, `r` ≥ 0.80 | 262 (45%) | 253 (97%) | 4 parallel, 4 relative, 1 fifth |
| any two agree | 540 (93%) | 484 (90%) | spread across all four |
| madmom and Essentia against keydetect | 92 (16%) | 67 (73%) | spread across all four |

On the 447 late-read runs "all three agree" was right 248 of 249 times. On LOW and WITHHELD songs
it never fired (0 of 11).

Against the human-source yardstick the same rules do worse: all three agree is right 134 of 142
(94%). The detectors share a weakness, the relative and the parallel key, and a page is what breaks
it. Seven songs show it: all three detectors agree, a page says otherwise, and the judge went with
the page (*Party in the U.S.A.*, *Fantasy*, *Faded*, *Run Away*, *How Low*, *Zero*, *So Fresh, So
Clean*).

## A page and the detectors together

Two tiers, everything else left for the person:

- **Tier A, backed by a page:** at least two of the three detectors name a key, and every key
  stated by a Hooktheory or Musicnotes page the lookup found is that key.
- **Tier B, detectors only:** the lookup found no page, and either all three agree or keydetect and
  one backer agree with `r` ≥ 0.80.
- **For the person:** a page names another key, the pages disagree with each other, or the
  detectors do not meet tier B.

With the pages the judge cited standing in for the lookup:

| | tier A | tier B | for the person |
|---|---|---|---|
| HIGH and MEDIUM (582) | 203 (35%), 203 agree | 186 (32%), 184 agree | 193 (33%) |
| HIGH only (260) | 185 (71%), all agree | 32 (12%), all agree | 43 (17%) |
| late-read runs (447) | 171 (38%), all agree | 146 (33%), all agree | 130 (29%) |
| LOW and WITHHELD (11) | 0 | 2, 1 agrees | 9 |

The two tier B misses are *Internet Baby* (A# major for the judge's G minor, a relative) and
*Superstition* (D# major for D# minor, a parallel). Against the human-source yardstick, tier B's
rule with the page ignored is right 149 of 162 (92%). That is the cost of a page the lookup fails
to find, so the lookup's recall matters.

## What the lookup finds

The fresh lookup of 120 songs first found only 33 of the 45 Hooktheory pages the judge had cited
for them. The misses were all one bug: a song judged before was looked up by the ledger's title as
stored, so a tail such as `(feat. Gwen Stefani)`, `(Remaster)` or `(7" Mix)` defeated the slug and
the search match. Fixed in `ec5db11`; with it the lookup finds 42 of 45 (Musicnotes 11 of 12).

On those 120 songs, with the fix:

| | tier A | tier B | for the person |
|---|---|---|---|
| songs | 29 (24%) | 43 (36%) | 48 (40%) |
| agree with the verdict | 28 | 42 | |

- The tier B miss is *Internet Baby* again.
- The tier A miss is *C.R.E.A.M.*: madmom, Essentia and the Hooktheory page say F# minor, keydetect
  and the verdict F minor. That verdict is an imported keytag-era HIGH with no human source and no
  tonic check, so it is more likely the verdict that is wrong.
- Ultimate Guitar is left out of the tiers on purpose. Its tabs stated the verdict's key 32 of 41
  times, and counting them only moved four tier B songs over to the person, without removing a
  miss. Musicnotes, found more often with the fix, stated another key on 8 of 28 songs; each of
  those goes to the person, which is the safe direction.

## What waits for the person

The 193 songs (of 582) left over with the cited pages:

| why | songs | the judge's key was among |
|---|---|---|
| no page; madmom and Essentia against keydetect | 51 | the three readings, 50 |
| no page; keydetect and one backer, `r` < 0.80 | 44 | the three readings, 44 |
| a page names another key than the detectors' pair | 35 | the pages, 25 |
| the pages disagree with each other | 24 | the pages, 22 |
| no page; the detectors split three ways | 23 | the three readings, 22 |
| a page, and the detectors split three ways | 16 | the pages, 15 |

Where there is no page, the judge's key was one of the three detector readings 116 of 118 times.
The person picks from what is already on the song page more than they work it out.

## Key changes

16 of the confident verdicts record more than one section, and 5 of them fall in tier A or B on
their main key. A rule gives one key for the whole track and never records a second key centre,
which leaves [#171](https://github.com/Bamo16/crateside/issues/171) (a second key centre needs
independent support) untouched: sections stay the judge's or the person's.

## Conclusion

Yes, it is possible for the larger part of a library. On these songs, rules alone rule on about
60–67% of confident songs and match the judge on 97–99% of them: 387 of 389 with the cited pages,
70 of 72 in the fresh sample, where one of the two misses is likely the verdict's own error. A page is what makes the
ruling safe against a relative or parallel swap. Without one, all three detectors agreeing is
still right 92–99% depending on the yardstick, which is the same footing as the judge's own
MEDIUM for a song with no human coverage. The remaining third to two fifths wait for the person,
and for most of them the answer is already one of the readings on the page.

The bands, how a ruling and the person's answer are recorded, and how they show are for the ADR.
