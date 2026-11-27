# The detectors on one set of notes, a page on the centre

*Note, 2026-11-06, frozen. Follows [the note of the same day on a lone detector backed by a page](2026-11-06-a-lone-detector-backed-by-a-page-against-a-relative-pair.md)
and the [note of 2026-11-03](2026-11-03-what-rules-can-rule-on-without-a-judge.md); measures a candidate
rule for [ADR 0076](../adr/0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md).
Decides nothing itself, and `VerdictRules` is unchanged. Ledger read from a copy of `ledger.db` as it
stood on 2026-11-06 (latest run per song); nothing was written to it. The page lookups were fresh,
through `SourcePages` itself, one request every two seconds.*

## The candidate rule

Every detector's key is K or K's relative; every Hooktheory or Musicnotes page that states a key
states one of those two, and they all state the same one, K\*. Rule K\*. By how many detectors name K\*:

| case | detectors on K\* | the other detectors | today |
|---|---|---|---|
| 1 | two | one, on the relative | tier A |
| 2 | one | two, on the relative | the person |
| 3 | none | all three, on the relative | the person |

All three on K\* is tier A as well. Cases 2 and 3 are what the rule would add.

## Method

- **Songs and reference:** as the lone-detector note: 582 songs with a HIGH or MEDIUM verdict by the
  judge and a reading from all three detectors; the reference is the judge's key collapsed to major
  or minor. Not ground truth. **MEASURED.**
- **Method check:** counting Hooktheory and Musicnotes pages the way the rules do (the key the judge
  recorded for the cited page), tier A is 173 of 173 and tier B 208 of 210, and case 2 is 8 of 8,
  exactly as the lone-detector note had them. **MEASURED.**
- **Pages in the ledger are the judge's, one key per page.** The rules read every section's key on a
  Hooktheory page, so a page that states two keys in different sections counts as stating both. The
  fresh lookup below is what shows the difference.
- **Intervals** are Clopper-Pearson 95%.
- **Bar:** tier A 387 of 389 (98 to 100%) and tier B 70 of 72 (90 to 100%) in the 2026-11-03 note;
  173 of 173 and 208 of 210 (97 to 100%) in this ledger.

## The judge's cited pages

| | songs | judge's key is the page's | judge's key is the detectors' |
|---|---|---|---|
| case 3 | 3 | 2 | 1 |
| case 2 | 8 | 8 | 0 |
| case 1 | 13 | 13 | 0 |
| all three on K\* | 120 | 120 | 0 |

**MEASURED.** None of the 11 songs in cases 2 and 3 is in tier A or B.

Case 3 in full:

- Alan Walker – *Faded* (`pIWaVJPl0-c`), HIGH: the detectors say F# major, Hooktheory and Musicnotes
  D# minor. Judge D# minor. An imported corpus verdict, verified there against both pages.
- Real McCoy – *Run Away* (`6xVs6vU_b7A`), MEDIUM: the detectors say C# major, Hooktheory A# minor.
  Judge A# minor.
- Porter Robinson – *Language* (`HDVIQ6E_J58`), MEDIUM: the detectors say A major, Hooktheory F#
  minor. Judge **A major**, with a chord argument: the page's chords are as diatonic to A major as to
  F# minor, and its tonic rests on naming i as F#m with no cadence to it. The rule would have said
  F# minor.

The rule, over the 11 songs it would add:

| judge's verdict | songs | agree | 95% interval |
|---|---|---|---|
| HIGH | 6 | 6 | 54 to 100% |
| MEDIUM | 5 | 4 | 28 to 99% |
| both | 11 | 10 | 59 to 100% |

**MEASURED.** Over the whole rule (cases 1 to 3 and all three on K\*): 144 songs, 133 of them already
tier A, 143 agree (96 to 100%); HIGH 134 of 134, MEDIUM 9 of 10. That figure is tier A's, and says
nothing about the 11. Counting Ultimate Guitar as a page, as the 2026-11-03 note did, adds one song
to case 2 and changes nothing else: 11 of 12.

Case 3 is rare (3 of 582, 0.5%). It is also the one with a split: the three detectors agree, and
the judge sided with the page twice and with the detectors once. **MEASURED.** *Faded* and *Run Away*
are the shape where a page rescues a relative swap; *Language* is the shape where the page is the
one that is uncertain.

## The fresh lookup

The songs: the 12 that have cited pages and fall in case 2 or 3 (11 counted by the rules, plus one
whose only cited page is Ultimate Guitar), and the 37 where the detectors are a lone one against a
relative pair with no cited Hooktheory or Musicnotes page, where a fresh lookup can add a case 2
song. 48 distinct songs, one being in both lists. **MEASURED.**

Not fetched: case 3 has no such list. A song with all three detectors agreeing and no cited page
could turn out to have a page on the relative, and there are 183 of them, over the 60 allowed. The
case 3 count above is therefore a floor. **MEASURED** count, outcome unknown.

All 48 lookups reached Hooktheory and Musicnotes. A Hooktheory or Musicnotes page came back for 16
of the 48: all 11 songs with cited pages that the rules count, and 5 of the 37 with none.

### A lookup bug, found and fixed on the way

*Party in the U.S.A.* first came back with no page on any site. The sites file it as
`party-in-the-usa` and the slug made `party-in-the-u-s-a`, so the guessed URL missed and the
search match, which compares slugs, missed too. Fixed in this session: a dot after a lone letter is
dropped (`U.S.A.` is `usa`, `J. Cole` is still `j-cole`). The song was looked up again alone. Every
number below is with the fix.

### Pages that changed

Of the 12 songs with cited pages, the fresh pages name the same keys as the cited ones on 10. The
two that differ:

- Porter Robinson – *Flicker* (`tLUeSfVcha4`): the Hooktheory page states D# minor in three sections
  and F# major in a fourth, so it states both keys of the pair. The ledger kept only D# minor. Out of
  case 2.
- Miley Cyrus – *Party in the U.S.A.* (`R1kOdTm9FBk`): Hooktheory F# major as cited, but Musicnotes
  now has a page stating G major. Out of case 2.

On the pages themselves rather than their keys, 6 of the 12 differ in which Musicnotes page was
found: for *Without You* and *Hello* a different product of the same song, for *Faded* and *Field of
Hopes and Dreams* the judge's Musicnotes page is not found, and for *Fantasy* and *Party* a page
the judge did not cite. Hooktheory's pages are the same on all 12. **MEASURED.**

### Re-measured on the fresh pages alone

| | songs | judge's key is the page's | judge's key is the detectors' |
|---|---|---|---|
| case 3 | 3 (*Faded*, *Language*, *Run Away*) | 2 | 1 (*Language*) |
| case 2 | 7 | 6 | 1 |
| case 1 | 3 | 2 | 1 |

Case 2 lost *Flicker* and *Party*, and gained one song the judge never had a page for:
Yellowcard – *Breathing* (`SucBpQiwYZU`), where keydetect reads E minor against G major from madmom
and Essentia, and both Hooktheory and Musicnotes state E minor. The judge, in September on an older
detector set and without those pages, took G major on the chords, HIGH, with an Ultimate Guitar
chart stating G. The rule would have said E minor. Which is right is not settled here; the point
is that the rule and the judge disagree on a HIGH. **MEASURED.**

Case 1 here is three songs the lookup newly gave a page, so they are tier A on fresh pages:
t.A.T.u. – *All the Things She Said* (`ujpvNvSgcic`) and Snail's House – *Pixel Galaxy*
(`RK8e4JQgPfo`) agree with the judge; Rare Earth – *Get Ready* (`ukaMFDSrAtQ`) does not: keydetect and
Essentia say D minor, the Musicnotes page D minor, the judge F major on the cadence (MEDIUM, with a
Sheet Music Direct arrangement for F major). Tier A's own figures were measured on cited pages; this
sample is one miss in three. **MEASURED** counts; **INFERRED** that the sample is not representative,
since all three songs were picked because the detectors split on a relative.

Cases 2 and 3 together, fresh pages only:

| judge's verdict | songs | agree | 95% interval |
|---|---|---|---|
| HIGH | 5 | 4 | 28 to 99% |
| MEDIUM | 5 | 4 | 28 to 99% |
| both | 10 | 8 | 44 to 97% |

Case 2 alone: 6 of 7 (42 to 100%). Over the nine distinct songs that were in case 2 on either the
cited or the fresh pages, the judge agreed with the page on 8 (52 to 100%).

## What it does not show

- **Ten and eleven songs cannot show 97%.** Even a perfect run of 11 has a lower bound of 72%. The
  bar can be missed by the count but not met by it. **INFERRED.**
- **The reference is the judge's.** For *Breathing*, *Language* and *Get Ready* the judge weighed a
  chord argument against a page, and the page may be right. Nothing here establishes which. A rule
  that follows pages is wrong where the judge was right, and the other way round.
- **The pages are a lookup's.** *Flicker* and *Party* show that a fuller lookup removes songs from
  the rule, and *Breathing* shows that a page the judge never saw can add one that disagrees with
  the judge. Each moves the numbers by a song or two out of ten.
- **Case 3's coverage is a floor** (see above).

## Recommendation

**Adopt neither.**

- **Not the full rule, and not case 3 in any band.** It is 2 of 3 on the judge's cited pages and
  2 of 3 on the fresh ones: the judge went against the page once, and the page there was weak on its
  own tonic. The rule stands on the page's key in exactly the situation where all three detectors say
  otherwise. HIGH is out for both cases: the HIGH sample is 6 of 6 on cited pages and 4 of 5 on fresh
  ones, and the one fresh HIGH miss is the shape the rule is exposed to.
- **Case 2 at MEDIUM is the best of the pieces and still not enough.** 8 of 8 cited and 6 of 7 fresh,
  8 of 9 distinct songs, against 208 of 210 for tier B. It would move 7 to 9 songs in 582 (1.2 to
  1.5%) from the person to MEDIUM, with the person's override still there. The gain is small and
  the one fresh miss is a HIGH-judged song on a page of two sites that agree.
- **What would change this:** a larger sample. The 183 all-agree songs with no cited page, looked up
  fresh, would give case 3 its real count; at the rate of this run (48 songs in about 35 minutes)
  that is about two hours of fetching, and it needs the go because it is over 60 songs. Later, the
  `DecidedBy = Rules` runs and the person's overrides would give case 2 its own measurement.

The bar is tier A's 387 of 389 and tier B's 70 of 72. Neither case meets it on the counts available.
