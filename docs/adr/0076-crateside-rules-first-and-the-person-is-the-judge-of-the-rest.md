# 0076 - crateside rules on a key first, and the person is the judge of the rest

- Status: Accepted
- Date: 2026-11-03
- For [#164](https://github.com/Bamo16/crateside/issues/164). Amends
  [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md) (the judge is no longer the only
  writer of verdicts) and [ADR 0069](0069-a-verdict-is-checked-against-its-evidence-before-it-is-recorded.md)
  (a rule's HIGH stands on a page, not a tonic check).

## Context

Judging is the only stage that needs an account, so a first-run user has no judge, and a judge that
is down (DeepSeek's suspension of 2026-11-03) stops every new track at "waiting to be judged". #164
first asked for a `none` judge giving "the detectors' agreed key". The maintainer reframed it: the
person using crateside is the judge. crateside shows the evidence and rules on what it can rule on
confidently by itself, and the rest waits for the person.

**Measured** in [the note of 2026-11-03](../notes/2026-11-03-what-rules-can-rule-on-without-a-judge.md),
over the 582 HIGH and MEDIUM verdicts with readings from all three detectors on keydetect 1.3.0:

- the two tiers below rule on 60–67% of songs (60% on a fresh lookup of 120 songs, 67% with the
  pages the judge cited) and agree with the judge on 387 of 389 and 70 of 72;
- tier A agreed on all 185 of the HIGH verdicts it covered;
- on the 447 runs whose readings were taken after the verdict, so the judge never saw them, both
  tiers agreed on all 317;
- where no page exists, the judge's key was one of the three detector readings 116 times in 118.

## Decision

### Who decides

Every verdict records who decided it, in a new ledger column `DecidedBy`:

| value | who | shown as |
|---|---|---|
| `Judge` | an agent session (`claude -p`, on whatever endpoint) | judged |
| `Rules` | crateside's rules, below | ruled by crateside |
| `Person` | the person, on the song page | your call |

A migration sets `Judge` on every existing run, the imported keytag history included, since an
agent decided those too. The ledger's own copy before a migration (`before-<migration>.db`) is the
backup. No existing verdict is re-ruled or re-judged.

### The rules

`VerdictRules`, a pure function of a track's evidence and its looked-up pages, returns a ruling or
nothing. It reads keydetect, madmom and Essentia's whole-track keys, keydetect's `r`, and the keys
stated by the Hooktheory and Musicnotes pages `SourcePages` found. Ultimate Guitar is shown but
never counted: it moved songs from tier B to the person without catching a miss.

- **Tier A, recorded HIGH:** at least two of the three detectors name one key, and every key the
  Hooktheory and Musicnotes pages state is that key, at least one page stating it.
- **Tier B, recorded MEDIUM:** no Hooktheory or Musicnotes page states a key, and either all three
  detectors agree, or keydetect and one backer agree with `r` ≥ 0.80.
- **Anything else, no ruling:** a page names another key, pages disagree with each other, or the
  detectors do not meet tier B.

Keys are compared collapsed to major or minor, as the ledger does (a page's `B dorian` is B minor).
A ruling records a center on the agreed key's tonic with mode Ionian or Aeolian, no sections, the
agreeing pages as sources (Hooktheory and Musicnotes' Original Published Key `Authoritative`, other
Musicnotes arrangements `Corroborating`), the readings that disagree as candidates, the evidence,
the lookups, and one plain reasoning line naming what agreed (`keydetect, madmom and Essentia name
D# minor; Hooktheory's page agrees.`). It has no tonic check.

ADR 0069's record-time check is for a judge's verdict file and does not run on a ruling: the
ruling's key comes from the evidence by construction, and its HIGH stands on two detectors and an
agreeing page in place of a tonic check, as measured.

A rule never records a second key centre, so sections stay the judge's or the person's, and
[#171](https://github.com/Bamo16/crateside/issues/171) (a second key centre needs independent
support) is untouched.

### The verdict stage, in order

For a new track or a Library backfill track, once its evidence is complete (ADR 0068):

1. **Look up the pages** (`SourcePages`, kept as `sources.json` in the working set as now). This
   moves out of the judge session's preparation and runs whatever judge is configured.
2. **Rules.** A ruling is recorded and the track goes on to separation like any judged track. No
   judge session runs, whatever judge is configured: the maintainer chose rules first always
   (2026-11-03), since they agreed with the judge on 97–99% of what they ruled on and spare about
   37k tokens a track.
3. **The judge**, if one is configured, gets what the rules left.
4. **The person**, if there is no judge: the track's Verdict job records the new status
   `AwaitingPerson`. It is not a strike and not a hold. The selector skips it, separation goes on,
   and its stems park as "no key", as a WITHHELD verdict's do, until a key is decided.

A re-judge, judging from bars, and the re-judge queue always go to the judge: the person asked
for a judge's reading. With no judge configured they are not offered.

Configuring a judge later sends it the `AwaitingPerson` tracks, which count as queued for
judging. Verdicts by rules or by the person are never re-judged on their own.

### The seam

`IVerdictJudge` has one method, `JudgeAsync(WorkOrder, JudgeDirection?, onStep, ct)`, returning
`VerdictResult` as `VerdictRunner.RunAsync` does now, and `VerdictRunner` becomes its Claude Code
implementation, unchanged otherwise. The setting `Judge:Kind` is `ClaudeCode` or `None`.
Unset, it is `ClaudeCode` when a `claude` executable is on PATH and `None` otherwise. `None`
registers no judge, and the drainer goes from rules straight to the person. There is no plugin
framework; another kind is added when someone needs it.

### The person's verdict

On the song page, a track with no taggable key (`AwaitingPerson`, WITHHELD, LOW) shows the
evidence and a choice: each distinct key among the three readings, keydetect's runner-up and the
pages' keys, each with who named it, plus any of the 24 keys. Any other song can be overridden the
same way ("Set the key yourself"). The choice is recorded as a ledger run with `DecidedBy =
Person`, confidence HIGH, the center on that tonic with mode Ionian or Aeolian, a source of kind
`Ear` stating the key, and the evidence and lookups the page showed. The latest run wins, so it
supersedes whatever was there, and parked stems are released by the existing `ReleaseParked`.

It is HIGH because the person is the judge of their own library and the key tag follows it; the
screen shows "your call", not a confidence band, for a `Person` run.

### Where it shows

- **Song page:** the verdict block names who decided (judged, ruled by crateside, your call); a
  ruling shows its reasoning line and the pages it stood on.
- **Tracks:** a track waiting for the person reads `waiting for you`, its detail line "decide its
  key on its page", and its verdict column marks a ruling or the person's call the way the song
  page does.
- **API:** `/api/tracks` and `/api/tracks/{id}` carry `decidedBy`, and `waitingForPerson` on a
  track's state.
- [#179](https://github.com/Bamo16/crateside/issues/179) (detector results on the song page before
  judging) is the natural home of the evidence the person decides from; this ADR needs only the
  choice and its sources.

## Consequences

- crateside works from download to tagged stems with no account, as #164 asks, with about two
  fifths of tracks waiting on one click from the person.
- With a judge configured, the judge sees only the third or so that the rules cannot settle, so
  judging costs about a third of what it does now.
- A wrong page with two detectors agreeing gives a wrong HIGH (*C.R.E.A.M.* in the sample, where the
  verdict itself looks wrong). A Hooktheory page for another recording is the risk; the person can
  override on the song page.
- Tier B carries the detectors' shared weakness, the relative and parallel key, where no page
  exists to catch it: right 92% against human pages in the measurement, the same footing as the
  judge's own MEDIUM for a song nobody has transcribed.
- The thresholds are fixed in code from one measurement. A detector build change does not re-rule
  anything, and the next measurement is read from `DecidedBy = Rules` runs the person overrides.

## Choices made in building it

Where the decision above was silent:

- **`Judge:Kind` is resolved, not registered.** `IVerdictJudge` is always registered as the Claude
  Code judge; an unset kind becomes `ClaudeCode` or `None` once at start (`DefaultJudgeKind`, from a
  `claude` executable on PATH), so `None` is what the drainer, the selector and the quota guard read
  from `JudgeOptions.IsConfigured`, and no registration holds a condition.
- **A ruling needs a name.** The rules record only for a song that has an artist and a title, from
  the ledger when it was judged before or else the row's `Artist - Title`; a row without either goes
  on to the judge or the person, as the page lookup could not run for it either.
- **A page the lookup could not reach is not an absence.** When Hooktheory or Musicnotes could not
  be reached, tier B does not rule (the page that disagrees may be there), and the lookup is not
  kept as `sources.json`, so the next attempt asks again. Ultimate Guitar's failure changes nothing.
- **Only the keys a page lists count**: each Hooktheory section's key and Musicnotes' Original
  Published Key. A key change inside a section, which the parsed tab shows, is the judge's to weigh.
- **A Musicnotes page whose note says "user arrangement" is `Corroborating`**; every other
  Hooktheory or Musicnotes page is `Authoritative`.
- **Separation did not wait for a verdict's absence to be settled, it waited for a verdict.** A
  track enters the GPU line only once the ledger has a run for it, so an `AwaitingPerson` track
  would have sat there. It now joins the line like a judged track, and its stems park as "no key".
- **The working set of an `AwaitingPerson` track is kept** by the janitor: its evidence and pages
  are what the person decides from.
- **A Library backfill track the rules leave open, with no judge, is `AwaitingPerson` too**, which
  is what stops the selector picking it again every pass. With no judge the ordinary verdict stage
  works the backfill whenever backfill is switched on, since a ruling costs nothing.
