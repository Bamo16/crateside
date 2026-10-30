# How thin are the Hooktheory sources the ledger leans on?

*Note, 2026-10-20, frozen. The count that [#124](https://github.com/Bamo16/crateside/issues/124)
(read everything a Hooktheory page offers) promised for
[#122](https://github.com/Bamo16/crateside/issues/122) (weigh how much a Hooktheory entry covers
before it caps a verdict). Decides nothing. Every figure is **MEASURED** unless tagged otherwise.*

## How it was counted

Every distinct `hooktheory.com/theorytab/view/…` URL in the ledger's `Sources` table: 189 pages
behind 201 source rows (187 `Authoritative`, 14 `Corroborating` among the view rows; one more row
is a `cheat-sheet` page and not a tab). Each page was fetched with the page-payload walk plus one
unauthenticated API call per section (keydetect-harness `scripts/hooktheory.py`, the reference
the C# port follows), at 0.3 s spacing. 184 pages answered; 5 returned 404 at the ledger's URL,
all slugs ending in a hyphen (`50-cent/many-men-`, `disclosure/waterfall-`,
`dj-fresh/hot-right-now-`, `lauryn-hill/doo-wop-`, `post-malone/sunflower-`), so those URLs were
stored wrongly or have since moved.

Contributors leave out the site's own `Hooktheory` account and `[deleted user]`. **Coverage** is
the union of the sections' `syncStart`–`syncEnd` spans that are synced to the page's own video,
as a share of that video.

## Is a coverage fraction a fraction of the video? (checked first)

**MEASURED** on six tabs against the video's real length (`yt-dlp --print duration`): in simple
time (Pink Floyd – *Money*, Disclosure – *Latch*, Paul Russell – *Lil Boo Thang*) the section's
beats at its tempo, divided by `syncEnd − syncStart`, give the video's length to the second. In
compound and half-time tabs (Billy Joel – *Piano Man*, Daft Punk – *Around the World*, JVKE –
*golden hour*) the same quotient is 2× or 3× off, which is the tempo's note value and not the
fractions. Across all 184 pages the spans are ordered, never overlap, and no union exceeds 1.
So a share of the *synced video* is sound. Whether the synced video is the recording being judged
is a separate question: 13 pages have a section synced to a different video than the page's own
(16 of 467 sections), and the page's video is not checked against the track's `source_url`
here (**INFERRED** to be the same for most).

## Contributors

| contributors | pages |
|---|---|
| 1 | 34 |
| 2 | 31 |
| 3 | 28 |
| 4–6 | 41 |
| 7–11 | 35 |
| 12 or more | 15 |

No page has 0. **34 of 184 (18%) are one person's analysis; 65 (35%) are one or two.**

## Sections mapped

| sections | pages |
|---|---|
| 1 | 54 |
| 2 | 44 |
| 3 | 41 |
| 4 | 31 |
| 5–7 | 14 |

**54 of 184 (29%) map one section; 98 (53%) map one or two.** Every section on every page is
synced (none unsynced).

## Coverage of the synced video

| share of the video | pages |
|---|---|
| under 10% | 52 |
| 10–25% | 67 |
| 25–50% | 56 |
| 50–75% | 7 |
| 75–90% | 2 |
| 90% or more | 0 |

Median 19.5%, mean 21.5%. **119 of 184 (65%) cover under a quarter of the recording; 52 (28%)
cover under a tenth; only 9 reach half.** A TheoryTab almost never maps the whole song.

## The combinations that matter for Rule 1

- **One contributor and one or two sections: 30 pages.** One contributor and under half the
  video: 32 pages.
- Of the 182 `Authoritative` source rows whose page answered: 33 are one contributor with one or
  two sections, 120 cover under a quarter of the video, 51 under a tenth.
- Of the 14 `Corroborating` rows: 1 is one contributor with one or two sections, 9 cover under a
  quarter.
- One section, one contributor and under a tenth of the video, as for fusq – *Perfume!*: for example
  Disclosure ft Sam Smith – *Latch*, Lil Wayne – *A Milli*,
  Outkast – *So Fresh, So Clean* and fusq – *Perfume!*.

## Key changes inside a section

7 of 467 sections (in 6 pages) modulate inside the section, which the page's single key per
section hides: Backstreet Boys – *Everybody* (B♭ minor → harmonic minor, beat 33), Beyoncé –
*Single Ladies* bridge (E minor → E major), JVKE – *golden hour* outro (E major → D♭ major), Pink
Floyd – *Money* chorus (B minor → B dorian), Queen – *Bohemian Rhapsody* bridge (A major → E♭
major) and outro (E♭ → F major), Seal – *Kiss from a Rose* bridge (F → G major). The site's own
whole-song summary lists more than one key for 32 pages.

## What it says for #122

Thin is the common case, not the exception: most Authoritative Hooktheory rows rest on a
quarter of the recording or less. Whether that should shrink Rule 1's cap, and from what
threshold, is #122's to decide; the work order now carries contributors, sections, coverage and
in-section key changes for the judge to read (#124).
