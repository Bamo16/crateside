# A lone detector backed by a page, against a relative pair

*Note, 2026-11-06, frozen. Follows [the note of 2026-11-03](2026-11-03-what-rules-can-rule-on-without-a-judge.md)
and measures one more rule for [ADR 0076](../adr/0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md).
Decides nothing itself. Read from the ledger as it stood on 2026-11-06 (a copy of `ledger.db`, latest
run per song); no judge was run, no page was fetched and nothing was written.*

## The pattern

Exactly one detector names key K; every Hooktheory or Musicnotes page that states a key states K,
and at least one does; the other two detectors agree on K's relative major or minor. Today the
rules leave such a song to the person: tier A needs two detectors on the page's key, and tier B
needs no page.

## What was measured

- **Songs:** 582 with a HIGH or MEDIUM verdict by the judge (the 11 `Rules` runs are left out, since
  they would agree with themselves) and a reading from all three detectors. The ledger holds 633
  songs now; the same 582 as on 2026-11-03.
- **Method check:** the 2026-11-03 tiers, run again over this ledger, reproduce that note's table
  exactly: tier A 203, all agree; tier B 186, 184 agree. **MEASURED.** There is no script in the repo
  for it; this one was written for the note and deleted.
- **Which pages:** the 203 and 186 only come out when "the pages the judge cited" means every human
  source the judge recorded as authoritative or corroborating, Ultimate Guitar included. The rules
  count Hooktheory and Musicnotes only, so the pattern is measured both ways:
  - **as the rules count** (Hooktheory and Musicnotes, `StatesKey` read): tier A 173, all agree;
    tier B 210, 208 agree;
  - **as the 2026-11-03 note counted** (any cited human source).
- **Reference:** the judge's key, collapsed to major or minor. A song the judge cited a page for
  was most likely judged by that page, so agreement here is largely agreement with the page. **INFERRED.**

## The result

| pages counted | covers | judge's key is K | the pair's relative instead |
|---|---|---|---|
| Hooktheory and Musicnotes, as the rules count | 8 of 582 (1.4%) | 8 | 0 |
| any cited human source, as 2026-11-03 | 10 of 582 (1.7%) | 10 | 0 |

**MEASURED.** None of the 8 is already covered by tier A or B, so the rule only moves songs out of
the person's pile: 8 of the 193 left over, 4%.

Split by the lone detector (as the rules count pages):

| lone detector | songs | judge's key is K |
|---|---|---|
| keydetect | 4 | 4 |
| madmom | 3 | 3 |
| Essentia | 1 | 1 |

By keydetect's `r` (its confidence in whatever it named, K when it is the lone one, the relative
when it is in the pair):

| minimum `r` | songs | judge's key is K | of which keydetect is lone |
|---|---|---|---|
| none | 8 | 8 | 4 |
| 0.70 | 8 | 8 | 4 |
| 0.75 | 4 | 4 | 2 |
| 0.80 | 2 | 2 | 0 |

The 8 songs, with the lone detector and `r`:

- Porter Robinson – *Flicker*: keydetect, D# minor, 0.79
- Harry Nilsson – *Without You*: madmom, E major, 0.73
- Miley Cyrus – *Party in the U.S.A.*: madmom, F# major, 0.90
- Mariah Carey – *Fantasy*: madmom, G major, 0.83
- Adele – *Hello*: keydetect, F minor, 0.73
- NewJeans – *Ditto*: Essentia, F# minor, 0.70
- Toby Fox – *Field of Hopes and Dreams*: keydetect, G minor, 0.75
- Miike Snow – *Genghis Khan*: keydetect, D minor, 0.78

Five of the eight (all five HIGH) have readings taken after the verdict, so the judge never saw
them; all five agree. Three are MEDIUM verdicts, all agree. **MEASURED.**

## What this does not show

- **Eight is few.** 8 of 8 is consistent with a true rate as low as about 63% at 95%. Tiers A and B
  were 387 of 389 and 70 of 72 on far larger counts. The bar of agreeing about as well is met by
  the count and cannot be shown by it. **INFERRED.**
- **The pages are the judge's, not a lookup's.** A page the judge did not cite could dissent. The
  pattern is only ever checked against cited pages, so a fresh lookup of these 8 songs is the one
  fetch that would test it: 8 songs, Hooktheory and Musicnotes, free sites, not done here.
- **Songs the pattern would catch but the cited pages hide:** 37 songs show a lone detector against
  a relative pair with no page cited (30 counting Ultimate Guitar as a page). A fresh lookup of those
  37 could add songs to the rule's coverage; it was not run. **MEASURED** count, outcome unknown.
- **Circularity.** For these songs the judge's verdict and the rule both follow the page. Where the
  page is for another recording (the *C.R.E.A.M.* shape in the 2026-11-03 note) the rule goes wrong
  with the judge. The person's override on the song page is the remedy, as for tier A.

## Recommendation

**Add it, recorded MEDIUM (tier B's band), with no minimum `r`.**

- It agreed with the judge on 8 of 8 and on 10 of 10 counting Ultimate Guitar, with no relative or
  parallel slip, which is the failure the pattern is exposed to. **MEASURED.**
- It stands on one detector and a page, less than tier A's two detectors and a page, so MEDIUM
  and not HIGH. **INFERRED.**
- A minimum `r` only discards agreeing songs: `r` of 0.75 leaves 4, 0.80 leaves 2, and the two
  highest-`r` songs (*Party in the U.S.A.*, *Fantasy*) are ones where keydetect sat in the wrong
  pair. **MEASURED.**
- It moves 4% of the person's pile and 1.4% of all songs, so the benefit is small and a wrong
  ruling is MEDIUM and overridable. Before it ships, run the fresh lookup of its 8 songs; if a page
  dissents on any, leave it out. Then read the next measurement from the `DecidedBy = Rules` runs
  it makes.
