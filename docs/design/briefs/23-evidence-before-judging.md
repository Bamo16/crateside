# crateside: what was read, before and after the verdict

Issue: #179

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Track details*, *Color means state*, and *Track details evidence* under
*Differs from the design*). The evidence panel was last designed in handoff 20
(`docs/design/handoffs/20-track-page.md`); screenshots of it are in
[`20-track-page/`](20-track-page/) (`takyon-2-evidence.png`, `starry-eyed-2-evidence.png`). The
pipeline change behind this brief is
[ADR 0096](../../adr/0096-the-page-lookup-is-part-of-the-evidence-run-and-keeps-what-it-passed-over.md);
the page choices are [ADR 0078](../../adr/0078-the-person-corrects-which-pages-count-for-a-song.md)
and [ADR 0081](../../adr/0081-the-person-removes-a-recorded-human-source.md). Terms (evidence,
evidence source, source kind, candidate, page choice, effective pages, working set) are in
`CONTEXT.md`.

---

## Why

The evidence panel was built all-or-nothing: it is the verdict's panel, headed *Why A minor*, and
the readings and pages live inside it. A track waiting for the judge gets a stand-in, *What was
found*, drawn by the same component with the verdict's parts blanked, and before the rule pass has
looked the song up it says "not looked up yet". With a long judge queue that is most of a new
track's life.

What was read and what was concluded from it are different things, written at different times by
different stages. The page should show them as two blocks: **Evidence**, there from the moment the
detectors have run, and **Verdict**, there once a run exists.

## The page today

Track details' evidence section (`Evidence.razor`, from `Song.razor`) is one panel with a head
(*Why A minor*, a subtitle, *Share*) and two columns:

- **Left, the argument**: *Centers*, *Why this key*, *Considered* or *Still open*, **Human
  sources** (the run's sources, with ADR 0078's *Not this song* · *Count this page* · *Add a page*
  and ADR 0081's remove), *First pass: withheld*.
- **Right, the measurements**: *Your note to the judge* and *Your ear*, *What the detectors
  reported* (Detector · Key · Score · Band · Runner-up), the backer line, *Tonic check*, *Chord
  progression*, *Tempo*, *Recorded …*.

Three variants: a run (as above); a pending track (`PersonsKey.PendingFor`: same panel, head *What
was found*, *Why the rules gave no ruling*, Human sources built from the lookup); and a verdict
from before crateside kept evidence (one sentence, nothing else).

## What is decided

### Two blocks, verdict over evidence

The section becomes two blocks, stacked, full width, in this order:

1. **Verdict**, only when the track has a run: the answer, so it comes first.
2. **Evidence**, whenever the track has evidence kept, in the working set or on its run.

Before a run there is only the Evidence block, at the top of the section. Each block is its own
`--surface` box with its own head. The two-column split inside each is yours to decide, but the
content splits as below, and nothing appears in both.

| | Verdict block | Evidence block |
|---|---|---|
| **Head** | `Why A minor` · `Why no key` (WITHHELD) · `Your call: A minor` (person); subtitle as today; **Share** | `What was read`, and a status line (below) |
| **Holds** | *Centers*, *Why this key*, *Considered* / *Still open*, **Sources it rests on**, *Tonic check*, *Your note to the judge*, *Your ear*, *First pass: withheld*, ADR 0078's "the pages changed after this verdict" | *What the detectors reported*, the backer line, the relative-split line, *Chord progression*, *Tempo*, **Pages found**, **What the rules said** (before a run only) |

Tonic check moves to the Verdict block: it is the judge's argument, not a reading. Your note and
your ear move with it: they are kept with the run and were given to the judge.

### The Evidence block's status line

One line under `What was read`, 13px `--text-60`, saying where the track stands. Color only where
the state rules allow it: teal while a stage runs, amber when it is the person's turn.

| state | status line | the block holds |
|---|---|---|
| **No evidence yet** | `Not read yet · downloading` / `· reading` (teal, the stage running) / `· waiting to be read` | nothing else; the block is one head and one line |
| **Detectors read, lookup running** | `Detectors read · looking the song up` (teal) | the detector table, chords, tempo; *Pages found* says `Looking up…` |
| **Rules left it open, waiting** | `Not judged yet · waiting for the judge` / `· judging paused` / `· decide it yourself` (amber, no judge configured) | everything, and **What the rules said** |
| **Judging** | `Not judged yet · the judge is reading it` (teal) | as above |
| **Any run** | `Read <date> on keydetect <build>` and, where it applies, handoff 20's `Read on keydetect 1.2.1, after this verdict.` | everything but *What the rules said* |
| **Judged before evidence was kept** | `Not kept: judged before crateside kept evidence` | nothing else |

*What the rules said* is the rules' own sentence for why they gave no ruling (today's
*Why the rules gave no ruling*), headed as such, or `The rules would rule C major, MEDIUM: …` when a
page choice has just made them able to and the ruling is about to be recorded. It is gone once a
run exists: a ruling's reasoning is the Verdict block's *Why this key*, and a judge's run makes it
history.

### The Verdict block in each state

| state | head | what is different |
|---|---|---|
| **Rules ruled** | `Why A minor`; subtitle `Ruled by crateside <date>` | *Why this key* is the ruling's one line; *Sources it rests on* are the counted pages; no tonic check and no first pass |
| **Judge withheld** | `Why no key` | *Still open* for *Considered*; *First pass* if there was one; handoff 18's invitation to judge from bars stays where it is (the header strip) |
| **Judged** | `Why A minor`; subtitle as today | everything, each source marked where it came from (below) |
| **Person's pick** | `Your call: A minor` | *Why this key* is replaced by one line, `You chose A minor on <date>.`, then the run it replaced folded under a `--line` rule as *Before your call: judged C major, MEDIUM* with that run's reasoning, quieter (as *First pass* is). No confidence band, as ADR 0076 says |

### Where the pages go

**Pages found** (Evidence block) is the lookup, site by site, exactly as the evidence run kept it:

- **Each site** gets one row: its name, then its status in words: `1 page` · `none found` ·
  `could not be reached: <error>` · `not looked up: no artist and title to search with`.
- **Its pages** under it, as Human sources draws a page today (the source-kind badge, the key it
  states, its note, *Not this song* / *Count this page*). A page the person rejected stays,
  struck through, with its undo. *Add a page by URL* sits at the foot of *Pages found*: a page
  choice corrects the lookup, so it lives with the lookup.
- **Title-only candidates** (ADR 0077) follow the site's pages, marked `unconfirmed` as now.
- **Passed over**: the search's other hits (ADR 0096), folded by default as
  `Passed over: 7 more search results`. Opened, one line each: artist – title, the site's
  duration where it gave one, and why it was passed (`artist is none the track credits`), in
  `--text-60`, each linking out. No actions: to count one, the person uses *Add a page*.
- **Once judged**, a page the verdict cites is marked `cited` (monochrome, a filled dot or a
  small label; your call). Nothing is marked before a run.

**Sources it rests on** (Verdict block) is the run's sources, each with where it came from:
`found by the lookup`, `found by the judge` or `added by you`, as a quiet tail on its line. *Found
by the judge* is the one to notice (the lookup missed a page the judge found; *Beware the Forest's
Mushrooms* is the known case), but it is not a state, so it stays monochrome. ADR 0081's remove
stays on these lines. A ruling's sources are always the lookup's or the person's.

### What gets stored, and where

Per ADR 0096: the lookup runs at the end of the evidence run and is kept as `sources.json` beside
`evidence.json` in the working set, written every time (an unreachable site included), with every
search hit it passed over. A run copies it into its `Lookups`. Where a source came from is derived
by comparing URLs, not stored. The page reads the working set before a run and the run after,
as now.

### The judge's prompt

Unchanged in what it carries: the effective pages and the title-only candidates, with the page
choices noted (ADR 0077, 0078). Passed-over hits are never sent. The judge no longer fetches pages
itself before its session, except to retry a site the evidence run could not reach.

## How it reads for a track Bamo knows

**Dave Wise – *Stickerbush Symphony*** (Donkey Kong Country 2, an imported FLAC, judged
2026-11-08). The readings, the page and the verdict below are from its ledger run; the waiting
states are what the page would have shown before it.

**Waiting for the judge.** No Verdict block. *What was read · Not judged yet · waiting for the
judge*.

- Detectors: keydetect **A minor**, r 0.777, trust band, runner-up C major (0.795); madmom
  **C major** 0.945; EDMA **C major** 0.772. Backer line: madmom and EDMA agree; both are the
  relative of keydetect's key.
- Chords: `C · F · Em · Bm · G · Em · Am` and its loops, folded as today.
- Pages found: **Hooktheory, 1 page**: `david-wise/stickerbush-symphony`, authoritative, states
  A minor (Intro; Verse and Pre-Chorus, 98 BPM), with its *Not this song*. **Musicnotes, none
  found. Ultimate Guitar, none found.** Passed over: whatever else the searches returned.
- What the rules said: the detectors split on the relative (C major from madmom and EDMA, A minor
  from keydetect and Hooktheory), so no ruling.

**Judged.** The Verdict block appears above: *Why A minor*, MEDIUM. *Why this key*: "keydetect
names A minor at r 0.777, but neither madmom nor Essentia backs it… Hooktheory, with 5
contributors, states A minor and the chords resolve to Am, so I go with the human source."
*Considered*: C major, "the relative, named by madmom and Essentia, but the chord loops resolve to
Am rather than C." *Sources it rests on*: the Hooktheory page, `found by the lookup`. Below, the
Evidence block is unchanged except its status line (`Read 2026-11-08 on keydetect 1.3.0`), the
Hooktheory page now marked `cited`, and *What the rules said* gone.

Had it been *Beware the Forest's Mushrooms* instead, Pages found would read **Hooktheory, none
found**, with its passed-over hits listed, and the verdict's Hooktheory source
(`yoko-shimomura/forest-maze`) would say `found by the judge`.

## Not in this brief

- **Measuring the passed-over hits** (whether artist, title, duration or sync coverage can sort a
  hit without the judge): a follow-up once the data is kept, as #179 says.
- **The unjudged-track label bug** filed beside #179.
- **The share card** (#152): it shares the verdict, so it stays on the Verdict block's head.
- **The Hooktheory tab's sections against the song** (#176's tab, drawn on the warp view): later.

## What to send back

The section at full width and below 56rem in each Evidence status (no evidence, reading, looking
up, waiting, judging, after a run, not kept) and each Verdict state (ruled, withheld, judged,
your call); *Pages found* with a site unreachable, a rejected page, a title-only candidate, the
passed-over fold open, and a `cited` page; *Sources it rests on* with each of the three origins.
Update the *Crateside Track Page* canvas or make a new one, as suits.
