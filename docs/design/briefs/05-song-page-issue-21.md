# crateside: song page design brief (issue #21)

For the Claude Design session already working on crateside's visual language. This is a brief for
one page. An implementing agent will port your design to Blazor afterwards, so give it layouts and
states rather than code.

---

## What crateside is, in one paragraph

A local web app (Blazor Server, dark only) that sits open beside Ableton Live. It takes songs off a
YouTube playlist, downloads them, has an agent judge each song's musical **key** and **BPM**, cuts
**stems** (vocal, instrumental, a drums "warp guide") on the GPU, and files the results into
`Sample Staging` with Ableton tags. The user then warps them by hand and moves them into
`Mashup Samples`, the finished library. The main screen is **Tracks**: one row per song. Each row
expands in place to show that song's files and controls.

## The page today

Route `/song/{VideoId}`, reached from a link on the expanded Tracks row that reads **Why this key**.
It shows only the key verdict's **evidence**:

- a header: the song's name, the subtitle *"Why this key was given, and what it was based on."*,
  and an *Open on YouTube* link
- **Why this key**: the judging agent's reasoning (prose), then a list of human sources (web pages,
  sheet music), each with a stance (agrees / dissents / says nothing), a kind (authoritative /
  corroborating / chord recogniser / algorithmic), and what it stated
- **What the detectors reported**: a small table of three key detectors (name, key, confidence or
  runner-up), a "relative split" warning when two of them landed on relative keys, a tonic check,
  the chord progression, and a key-events timeline

## What is wrong with it (the user's words, from #21)

- **It reads as a recolour, not a design.** The visual language was applied to a layout that
  predates it.
- **It buries its own answer.** The page exists because a key was decided. The **key**, the
  **confidence** and the **BPM** are the conclusion and should read as the conclusion. Today they
  are not what the eye lands on first.
- **Its name is too narrow.** It should be *everything crateside knows about a track*: its state,
  its stems, where they are, what has run and what has not, alongside the verdict and its evidence.
  The evidence becomes one section, not the whole page.

## Acceptance criteria (from #21)

- [ ] The key, confidence and BPM are the first thing read
- [ ] The page covers the track's state and stems, not only the verdict's evidence
- [ ] The evidence remains reachable and complete, as a section rather than the whole page
- [ ] The link on the row, and the page title, say what the page is now for (not *Why this key*)
- [ ] It looks designed for the current visual language rather than adapted to it

---

## What the page can show

All of this already exists on the row's data model (`TrackRow`) or the evidence record
(`RunEvidence`). You don't need to invent data. Choose which of it earns a place.

**The conclusion**
- Key, e.g. `G# minor`, possibly with a **mode** (`D Dorian`) when the song is modal and the tagged
  key is a simplification ("collapsed")
- Confidence: `HIGH`, `MEDIUM`, `LOW`, `WITHHELD` (only HIGH and MEDIUM are tagged)
- BPM, e.g. `128`; the runner's own rough estimate is also kept and can conflict (off by 2× is
  normal and not a conflict)
- When it was judged

**Where the track is**
- A **state**, shown on Tracks as a chip: queued, downloading, detecting key, judging, judged · in
  line (for the GPU, with a position), separating, staging, tagging, ready to warp, parked (poor
  audio / no key), gave up (stage × strikes), short (files missing), done
- A one-line **"what happens next"** sentence per state
- While something runs: which stage, and for how long
- Findings the user must answer: parked for poor audio (*Use it anyway*), "looks like a song
  already judged" (*Same song / Different song*), a tag that disagrees with the verdict, a rename
  that did not finish

**What it has on disk**
- Each **sample**: label (`Vocal - Full`, `Instrumental - Full`, `source`, an added or adopted
  source's own label), whether it is in staging or already in the library, the key Ableton holds on
  it, and whether it came in **below premium** audio
- Whether a **warp guide** (drums) is still there, which means warping is outstanding
- The folder, and how long it has been in staging
- Samples crateside sent to the recycle bin, and why
- The **plan**: which presets the user wants made, whether the source is kept, and any added
  sources and adopted samples

**Its uploads**
- A song can have **several uploads** (a swap to a better one, added official acapellas). Each
  sample knows which upload it came from. *Open on YouTube* currently points at the primary one.

**Its genre** (new today)
- The settled genre from a short list (Drum & Bass, Future Bass, Hip Hop, J-Pop, K-Pop, Pop, Rock),
  or a suggestion awaiting the user, plus what the lookup sources called it

**The evidence**: everything the page shows today, unchanged in content.

## States worth designing

1. **Done**: judged, cut, everything in the library. The most common, and the one where the
   conclusion should shine.
2. **Ready to warp**: in staging, tagged; the user's turn.
3. **In flight**: a stage running (separation takes ~6–7 minutes).
4. **Never judged**: no verdict yet, so no conclusion to lead with. Today this shows only a notice.
5. **Parked** or **needs an answer**: poor audio, or "looks like an already-judged song".
6. **Library-only**: files in the library that predate crateside, never judged (about 90 of these).
7. **Several uploads**: samples from more than one upload.
8. **Judged but no evidence recorded** (older runs): the conclusion exists, the evidence does not.

## The visual language (don't invent a new one)

Defined in `src/Crateside/wwwroot/app.css`. The short version:

- **One dark, warm palette.** `--bg #141310`, `--surface #1b1a15`, `--raised #23211a`, text
  `--text #ece7dc`, with alpha tiers `--text-75/60/50` for prose and `--text-40/30` for labels
  and decoration only (below AA).
- **Colour means something, and nothing else is coloured.** Teal `#6fc7d6` = the machine is
  working. Amber `#e8b45a` = it is the user's turn. Red `#e07a6d` = needs attention. Green
  `#7fcf9a` = done, only ever done.
- **IBM Plex Sans** for text, **IBM Plex Mono** for every number, key, timestamp and count (read as
  values, compared down a column).
- **State chips** (from Tracks): neutral outline (nothing yet), teal filled with a dot (working now),
  teal outline (will work, not yet), amber filled (your turn), red outline (stopped, nothing
  automatic moves it), red filled (stopped after trying), green outline (finished), dashed (a
  deliberate absence).
- **Page headers** come in three shapes: *Bar* (brand and tabs), *Flush*, and *Shade* (sticky,
  carrying live activity, used by Tracks). The Library page uses *Bar*.
- Tables are width-capped per shape (Library ~56rem, Partners 50rem, Tracks 92rem), because
  a wide table makes the eye travel between related values.

## Constraints

- Blazor Server with scoped CSS per component. Dark only. Runs locally, no external assets (the
  fonts are self-hosted).
- **This page is for reading.** Every control that changes a track already lives on the expanded
  Tracks row. Whether any of them should appear here too is a question for you and the user. If
  they do, they are the same actions, not new ones.
- The evidence component (`Evidence.razor`) is reused as is if that suits the design, or restyled.
  Its content stays complete.

## What to send back

- The page in the **Done** state at full fidelity, plus how it changes for the other seven states
  above
- A proposed **name** for the page and its link text
- Anything in the data list you'd leave off, and why
- Open questions for the user

## Questions already worth asking the user

- Should the page offer the row's actions (re-cut, swap, rename, genre), or stay read-only and link
  back to the row?
- Is *Open on YouTube* per upload, or just the primary?
- For a never-judged track, what leads the page instead of the conclusion?
