# crateside: a verdict shows what it rests on, and its confidence is calculated from that

Issue: #214 (a verdict shows what it rests on, and its confidence is calculated from that)

A design brief, not a page design: the song page's part is words (below), so no Claude Design session
is needed. The maintainer reviews it; then an ADR, then the build. ADRs touched: 0076 (rules first,
the person decides the rest, and its 2026-11-20 madmom amendment), 0069 (a verdict is checked
against its evidence), 0095 and 0100 (a second key centre needs independent support), 0078 and 0081
(the person corrects pages and sources). Read beside keydetect-harness's `corpus_schema.md`
(`tier`, `verification.confidence`) and `docs/adding-tracks.md` §1a–§3. No verdict is re-judged
by any of this.

## The proposal

- **Every verdict has a basis**: which of keydetect, madmom and Essentia agree with its key and which
  dissent; which pages agree and which state another key, each page ranked **gold, silver or
  bronze** in keydetect-harness's words; whether the person set the key (**your call**); and the
  **judge's part**, named only where the judge did something the evidence could not: it decided
  **over a dissent**, or it placed a second centre that something independent confirmed (ADR 0095).
- **Confidence is calculated from the basis by one table** (below), the same for the rules, the judge
  and the person. Who decided never enters it. It is derived when the verdict is read, never
  stored, so a rule change re-grades every old verdict with no re-judge, as ADR 0095 already does
  for sections.
- **The song page shows the basis as a line** ("Hooktheory + 3 detectors", "3 detectors, no page",
  "2 detectors + a dissent", "your call", "judge, over a madmom dissent"). It never shows a bare
  grade. The grade stays where something acts on it: tag or park, the Key caveat Collection, and
  research selection.
- **One vocabulary with keydetect-harness:** crateside ranks each page with the harness's tier table,
  and computes the harness's own confidence (`high`/`medium`/`low`) from the pages alone as the
  basis's **corpus confidence**. Detectors never enter that half: the harness measures them, so
  counting them would be circular. Measured on the 62 corpus tracks found in the ledger, crateside's
  corpus confidence is never above the corpus's own.
- **What it changes, measured on the ledger:** of 696 latest verdicts, 207 change level (192 down,
  15 up). 113 drop below the tag line and 4 rise above it. 102 of the 113 have no page recorded at
  all, mostly judged from July to October. Before the switch, the build refreshes their pages and
  reads their missing detectors, and the maintainer sees what is left as a one-off review list
  (Bamo's call 1).

## Today, side by side

| | crateside today | keydetect-harness |
|---|---|---|
| **what counts** | keydetect, madmom and Essentia; Hooktheory and Musicnotes pages (the rules); any human source, a tonic check and the ear (the judge) | human sources only. Detectors and the sites that copy them are never sources |
| **how a page is classed** | a **source kind** (authoritative, corroborating, chord recognizer, algorithmic): "a kind, not a rank" (CONTEXT.md) | a **tier** (gold, silver, bronze): a rank |
| **Hooktheory** | authoritative whatever its size; a `THIN PAGE` (one contributor, one or two sections) is exempt from the judge's cap | gold with many contributors, silver with 1–2 |
| **Musicnotes** | authoritative for the Original Published Key, corroborating for an arrangement | gold only for a licensed publisher edition stating the Original Published Key; a user arrangement is no source |
| **Ultimate Guitar** | corroborating; never counted by the rules | usually bronze |
| **the ear** | a hint, or a human source by setting; the person's own call is HIGH by decree | "single aural verification" is bronze |
| **HIGH / high** | rules: two detectors, every page agrees, at least one page. Judge: two detectors, a tonic check and an agreeing human source, nothing authoritative against | two or more gold sources agree |
| **MEDIUM / medium** | rules: no page, and all three detectors, or keydetect and a backer at `r` ≥ 0.80 with no confident madmom dissent. Judge: "one leg missing" | one gold, or two silver |
| **LOW / low** | judge: "a relative or fifth split unresolved"; a cap for leaning on an unconfirmed section | one silver, or bronze only |
| **a dissent** | rules: any page with another key, no ruling. Judge: an authoritative one caps at MEDIUM | sheet music or contributor-analysed Hooktheory wins, else the track is rejected |
| **who decided** | `DecidedBy` is recorded, and sets the recipe that gives the grade | `verified_by` is recorded and sets nothing |
| **what acts on it** | HIGH or MEDIUM tags the key; LOW or WITHHELD parks the stems and adds the Key caveat; a HIGH-only filter in research counts (`KeyDetectorCounts`) | the admission floor: `high` in, `medium` only with a genre bucket other than `pop`, `low` out |

**Where they agree.** Both refuse algorithmic sites. Both compare keys collapsed to major or minor.
Both want two kinds of evidence that could fail independently. Crateside's thin page is a silver page
in the harness's words. A Musicnotes Original Published Key is authoritative in one repo and gold in
the other. The harness's ledger route (§1a: "two agreeing human-analysis sources on different sites")
already selects on a basis, not on crateside's grade.

**Where they differ.** Crateside's kind is not a rank, so a one-contributor Hooktheory page and a
published score count the same. Crateside's grade counts detectors and the harness's never does. And
crateside has two recipes behind one word: the rules' HIGH is a recipe from evidence, the judge's
HIGH is its own reasoning held to a check, and the person's HIGH is a decree.

## The basis

Four parts, each derived from what the ledger already stores. None is a new column: like
`Section.Support` (ADR 0095), the basis is computed when a verdict is read, because its inputs change
after the run (late readings, the person's page choices, a removed source).

| part | what it says | read from |
|---|---|---|
| **readings** | each of keydetect, madmom and Essentia: **agrees**, **dissents** (with its key and confidence), or **unread** (no reading stored). A **confident dissent** is madmom at 0.80 or more, the one dissent ADR 0076's amendment measured as worth stopping for | `Runs.Evidence`, late readings included |
| **pages** | each human page that states a key: **agrees** or **states another key**, with its **tier**. A page that states the verdict's key for one section and another key for another agrees, and places that section (ADR 0095's human-source leg) | `Sources` (authoritative and corroborating) and the pages in `Lookups` the run did not record, less what the person rejected or removed (`PageChoices`, `SourceRemovals`) |
| **the person** | **your call**: the person set the key (`DecidedBy = Person`, with its `Ear` source). **Your ear**: what they said they hear, sent with bars | `Runs`, `Sources` of kind `Ear` |
| **the judge's part** | **over a dissent**: a judge's key that fewer than two detectors name, that a confident madmom dissent stands against, or that a counted page contradicts. **Placed a second centre**: a section outside the primary whose support is a human source, a window the maintainer gave or a key lift (ADR 0095, 0100) | `DecidedBy = Judge`, the two parts above, `Section.Support` |

**Tiering a page**, by the harness's table (`docs/adding-tracks.md` §2–§3), from what crateside stores:

| page | tier |
|---|---|
| Hooktheory, 3 or more contributors | gold |
| Hooktheory, 1–2 contributors, or contributors not stored | silver |
| Musicnotes, Original Published Key (authoritative) | gold |
| Musicnotes arrangement (corroborating) | bronze |
| Ultimate Guitar | bronze (ratings are not stored, so never the harness's "high-rated chart") |
| other authoritative (a theory analysis that argues from cadence, published sheet music) | silver |
| other corroborating | bronze |
| algorithmic sites, chord recognizers | not a page: never counted, for or against |

"Many contributors" in the harness is read as three or more. Contributor counts are in `Lookups` only
for runs from 2026-11-01 on (72 of 696). For older runs the Hooktheory page counts as silver, the
floor that is certainly true, until the build fetches each cited TheoryTab once for its contributors
and keeps them beside the runs (a run is never rewritten).

**The ear.** With the Ear setting at `Hint`, your ear is shown and counts for nothing. At
`HumanSource` it counts as a silver page in crateside, and as bronze in the corpus confidence,
because that is what the harness calls it.

### Names, for both repos

**crateside `CONTEXT.md`**, under Keys:

- **Basis**: what a verdict rests on: its readings, its pages, the person's call or ear, and the
  judge's part. Derived when the verdict is read. Shown on the song page instead of a grade.
- **Agrees / dissents / unread**: a detector's stance on a verdict's key. **Confident dissent**: madmom
  dissenting at 0.80 or more.
- **Page tier**: gold, silver or bronze, keydetect-harness's ranking of a page, set by its site and
  what the page shows (contributors, edition). Beside the **source kind**, which stays as the judge
  records it.
- **Corpus confidence**: keydetect-harness's `high`, `medium` or `low`, computed from a basis's pages
  alone; a page stating another key makes it **disputed**.
- **Your call**: the person set the key. **Judge's part**: "over a dissent", or "placed a second
  centre".
- **Confidence** is redefined: HIGH, MEDIUM or LOW, calculated from the basis. **Withheld** becomes
  an outcome ("no single key to give"), not a confidence.

**keydetect-harness** keeps its words; they become crateside's. One paragraph for §1a of
`docs/adding-tracks.md`, for the harness's own maintainer to add: crateside's API carries each page's
tier and the corpus confidence, which make a lead list (select on "corpus confidence medium or
better"); they are leads, and §1a's re-tiering from the fetched pages still decides.

## The calculation

The first row that matches gives the confidence. A page here is a gold or silver page; bronze pages
are shown and never counted. Keys are compared collapsed to major or minor.

| # | basis | confidence | what it rests on |
|---|---|---|---|
| 1 | your call | HIGH | the person is the judge of their own library (ADR 0076). OWNED |
| 2 | two gold pages agree, none states another key | HIGH | the harness's `high`. OWNED |
| 3 | a page agrees, none states another key, two or three detectors agree | HIGH | tier A: 185 of 185 (ADR 0076). MEASURED |
| 4 | a page agrees, none states another key, one detector agrees | MEDIUM | a lone detector and a page against a relative pair: 8 of 8 (note of 2026-11-06). MEASURED, small |
| 5 | one gold page or two silver agree, none states another key, no detector | MEDIUM | the harness's `medium`. OWNED |
| 6 | one silver page agrees, no detector | LOW | the harness's `low`. OWNED |
| 7 | a page states another key; two detectors agree, or more gold pages agree than dissent | MEDIUM | the judge prompt's cap, now for everyone. OWNED |
| 8 | a page states another key, otherwise | LOW | |
| 9 | no page; all three detectors | MEDIUM | 99.0% against the judge (note of 2026-11-19). MEASURED |
| 10 | no page; keydetect and one backer, no confident madmom dissent | MEDIUM | 93.3%, and 96.8% at `r` ≥ 0.80 (same note). MEASURED |
| 11 | no page; keydetect and one backer, madmom dissents at 0.80 or more | LOW | right on 2 of 6 (ADR 0076's amendment). MEASURED, small |
| 12 | no page; madmom and Essentia against keydetect | LOW | 71.4% (note of 2026-11-19). MEASURED |
| 13 | no page; one detector or none | LOW | |
| — | the run withheld | WITHHELD, an outcome | |

A second centre never changes the confidence: the confidence is the primary's, on the primary's own
evidence. That is what ADR 0095's "leans on a hypothesis" cap approximated, so the cap goes. The
same goes for the HIGH check of ADR 0069 (`MissingForHigh`): a judge's key gets HIGH exactly when
its basis does. The judge prompt stops asking for a confidence: the judge writes a key, or WITHHELD.

The rules' thresholds for deciding without a judge (`r` ≥ 0.80 for tier B) are about who decides and
what it costs, not about confidence. They stay as they are; a judge's verdict on the same evidence
gets the same confidence the rules' would.

### Run over the ledger

**MEASURED** on a copy of `ledger.db` from 2026-11-23, latest run per song, 696 verdicts, by a
scratch script (since deleted) that implements the tables above. Nothing was written to the ledger.

| today → calculated | HIGH | MEDIUM | LOW | WITHHELD |
|---|---|---|---|---|
| **HIGH** (273) | 175 | 79 | 19 | |
| **MEDIUM** (380) | 11 | 275 | 94 | |
| **LOW** (12) | | 4 | 8 | |
| **WITHHELD** (31) | | | | 31 |

- **207 change level: 192 down, 15 up.** 190 of the downs and all 15 ups are the judge's; 2 downs are
  the rules', and the person's one verdict stays HIGH.
- **The tag line: 113 fall below it, 4 rise above it.** Of the 113, 102 have no page recorded, 7 have
  a page stating another key, and 4 have a page but no detector. 75 carry the judge's part ("over a
  dissent"). 6 lack madmom and Essentia readings. By month judged: 3 July, 26 August, 40 September,
  33 October, 11 November.
- **79 HIGH → MEDIUM** stay tagged: nearly all are three detectors with no page, which the judge
  called HIGH before ADR 0069's check existed.
- **The judge's part shows on 139 verdicts.** 17 verdicts have a section outside their primary;
  which of them show "placed a second centre" is for `SectionSupports` to say (ADR 0095 counted 2 on
  2026-11-12).

Examples:

- **Down across the line, judge over a dissent.** Wu-Tang Clan – *C.R.E.A.M.* (`or5C2jV1qRc`):
  HIGH → LOW. No page, keydetect alone, madmom and Essentia both read F# minor. ADR 0076 already
  named it as a verdict that looks wrong.
- **Down across the line, judge over two pages.** Seal – *Kiss from a Rose* (`lkzXi4cmdJk`):
  MEDIUM → LOW. Hooktheory and Musicnotes both state G minor; one detector holds the verdict's key.
- **A rule change re-grades a ruling.** Soulja Boy Tell'em – *Crank That* (`OZzNLjlsWn4`): the rules'
  MEDIUM → LOW. Ruled on keydetect and Essentia while madmom dissented at 0.81, the case ADR 0076's
  amendment now stops.
- **A re-read reading re-grades a ruling.** Boys Noize – *Sireneh* (`I-S_KVXMyjg`): the rules'
  MEDIUM → LOW. keydetect's stored reading now dissents (F# major), leaving madmom and Essentia
  against it (row 12).
- **An old verdict made honest.** Genesis – *Invisible Touch* (`hHdBRygLBxc`): HIGH → LOW. madmom
  and Essentia against keydetect's D minor, the relative; its one page is a Musicnotes arrangement,
  bronze. The kind of verdict the review list exists for.
- **A page the refresh could lift.** Kendrick Lamar – *HUMBLE.* (`H4RELGc9su8`): MEDIUM → LOW. One
  Hooktheory page with no contributor count stored (so silver) and no detector agreeing; if the page
  has three or more contributors, it is gold and row 5 gives MEDIUM.
- **Up, same basis as a ruling.** Katy Perry – *California Gurls* (`ru3tYeyIOFk`): MEDIUM → HIGH.
  Hooktheory and two detectors, which is tier A.
- **Up across the line.** The Prototypes, Lowes – *Reason* (`4-FRF5obguQ`): LOW → MEDIUM.
  keydetect and madmom agree, Essentia dissents (A major, 0.80); row 10 tags it.
- **Down, still tagged.** Nicki Minaj – *Super Bass* (`dzuQ5ustJVM`): HIGH → MEDIUM. Three detectors,
  but the Hooktheory page the judge recorded states G# minor.

**Against the harness corpus (MEASURED).** 62 corpus tracks are in the ledger. Crateside's corpus
confidence equals the corpus's on 16, is lower on 41 (the harness found more sources, or counted
contributors crateside does not store), and is disputed on 5; it is never higher. The calculated
confidence is HIGH on 46 of them, all 46 on the corpus key; of 16 MEDIUM or LOW, 4 are not on the
corpus key. So the harness's confidence can be computed from crateside's basis as a floor, and
exactly only where crateside holds every page the harness used and its contributor count. It
cannot be exact in general, since the harness re-reads and adds pages by design.

## The song page

The verdict block's grade word ("high", "medium", "weak", from `KeyText`) gives way to one basis line,
then the details folded under it.

- **The line**, built from the parts in a fixed order: pages, readings, dissents, the judge's part.
  - "Hooktheory + Musicnotes + 3 detectors"
  - "3 detectors, no page"
  - "keydetect + madmom, Essentia dissents (A major)"
  - "Hooktheory + 2 detectors; judge, over a madmom dissent (F major, 0.84)"
  - "keydetect alone; judge, over madmom and Essentia (F# minor)"
  - "your call", or "your call, against 3 detectors (C minor)"
  - "keydetect alone; madmom and Essentia never read it"
  - WITHHELD: "no single key: keydetect C minor, madmom and Essentia A# major"
- **A second centre** reads "chorus in E major: placed by the judge, confirmed by Hooktheory"; an
  unconfirmed one keeps today's "possible key change (the judge's reading, unconfirmed)".
- **The details**, folded: each detector with its key and confidence and its stance; each page with
  its site, what it states, its tier word and why ("Hooktheory, 2 contributors: silver"); bronze
  pages listed as "shown, not counted"; and the corpus confidence ("as keydetect-harness would rank
  these pages: medium").
- **Tracks** shows the key or "no key" as now, and in place of the grade word one of four basis words:
  **page-backed**, **detectors only**, **over a dissent**, **your call**. The research filters use the
  same words.
- **The API** carries `basis` on `/api/tracks/{id}` and a short form on `/api/tracks`: the line, each
  reading and page with its stance and tier, the person's part, the judge's part, the corpus
  confidence, and `confidence` as calculated. The confidence the decider wrote stays in the ledger,
  and in the API as `recordedConfidence`, as history.

## Settled

- **Derived, not stored.** The basis and confidence are computed on read, like `Section.Support`. The
  ledger's `Confidence` column keeps what the decider wrote and nothing acts on it.
- **A missing reading is not a dissent.** Unread detectors count for nothing; the build first reads
  them wherever the source is on disk (`DetectorBackfill`).
- **Pages the run did not record still count.** A lookup page the judge left out is a page stating
  its key, unless the person rejected it (ADR 0078). The judge setting a page aside as another
  recording is its part ("over Hooktheory"), visible, and the confidence still counts the page.
- **A thin page is silver**, and a silver page stating another key caps at MEDIUM, like any other.
  This drops the judge prompt's thin-page exemption.
- **Unknown Hooktheory contributors count as silver** until the one-off fetch fills them.
- **The ear** is nothing at `Hint` and a silver page at `HumanSource`, bronze in the corpus confidence.
- **No finer scale and no new binary**: tag or park is the binary, as #214 says.
- **The judge writes no confidence.** It writes a key or WITHHELD; the record-time caps that tried to
  hold its grade to the evidence go, since the grade is now the evidence's.

## For Bamo to decide

1. **The switch-over.** 113 verdicts drop below the tag line: they lose their key on the Tracks page
   and gain the Key caveat in Live (key tags already written stay). Recommendation: refresh their
   pages and read their missing detectors first, recount, then give the rest to Bamo once as a review
   list with each basis and a blank for his call. Nothing parks before he has seen it; after that
   the calculation acts on its own.
2. **Your call is HIGH whatever the evidence says.** Recommendation: yes. The tag follows the person
   (ADR 0076), and the line shows when the detectors dissent. In the harness's terms it is bronze.
3. **keydetect and one backer below `r` 0.80, no page, is tagged (MEDIUM).** Recommendation: yes,
   93.3% against the judge. LOW would park 31 more verdicts, and keep parked the 4 that row 10
   lifts.

## Next

- **The ADR** records the basis and its parts, the tier table, the calculation table, and what
  goes: the judge's confidence field, the HIGH check of ADR 0069, ADR 0095's hypothesis cap, and the
  person's HIGH by decree. It amends ADR 0069, 0076 and 0095.
- **The build**: one pure `VerdictBasis` from a run, the person's page choices and the stored page
  facts, with `Confidence` derived from it; the one-off contributor fetch and detector read; the
  review list; the song page line and details, the Tracks word and the API fields; `CONTEXT.md`'s
  terms; the judge prompt without confidence. Tests cover a rules verdict, a judge verdict, a person
  verdict and a verdict with madmom and Essentia unread, as #214 asks. For Bamo's check, three
  tracks: Nelly Furtado – *All Good Things (Come to an End)* (`THph5Lx2FOo`, page-backed, with
  madmom dissenting on the relative), Alohaii,
  Kiyon – *Embrace* (`xl2nLBD9HTk`, detectors only) and Crystal Waters – *Gypsy Woman*
  (`NqThf-MpCjs`, judge over a madmom dissent). The build confirms each is a library track first.
