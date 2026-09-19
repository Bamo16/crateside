# Context — crateside

Glossary for the helper bench that supports the **human-limited steps** of the
user's Ableton mashup workflow. Glossary only — no implementation detail.

Terms shared with the detection tool — **Track**, **Stem**, **Sample**, **Warp
guide**, **Song identity**, **Verdict**, **Source video ID**, **Queue**, **Fully
processed**, **Sample Staging**, **Mashup Samples** — are defined once in
`keytag`'s `CONTEXT.md` and are not repeated here. This file covers what is
specific to crateside: the mashup-pairing vocabulary, and the views.

## Pairing

The domain of "what else can I play over this?". A **source key** is the key of
the thing the user already has in hand; everything below describes candidates
against it.

- **Partner** — a key that pairs acceptably with the source key. Partnership is a
  relationship between *keys*, not between tracks: a partner names a key to go
  looking for, and the looking happens in Ableton's browser.
- **Shift** — how many semitones a partner must be pitch-shifted to sit with the
  source, signed, normalized to ±6. **The dominant cost in the model**, and the
  first sort key: an unshifted pairing can be auditioned in seconds, whereas
  anything beyond ±1–2 semitones usually sounds wrong, so a small shift beats a
  close harmonic relationship. When several transpositions reach the same partner,
  the one with the smallest magnitude is the shift.
- **Alt shifts** — the other transpositions that reach the same partner, a fifth
  away in either direction (±7, normalized). Alternatives, not extras: use one
  *instead of* the shift, when it suits the material better.
- **Circle distance** — how many steps apart two keys sit on the circle of fifths,
  wrapped so it never exceeds 6. The harmonic half of the ranking, subordinate to
  shift.
- **Relationship** — what a partner *is* to the source in musical terms: the same
  key, its relative, a step around the circle in either direction, or a borrowing.
  Distinct from **shift** and **circle distance**, which are costs; this is a
  description.
- **Camelot code** — the harmonic-mixing notation (`8A`, `5B`, …) that numbers the
  circle of fifths and letters the mode. An alternate spelling of a key, not a
  separate concept.

## Import

- **Import** — finishing a song: warping it, tagging it, saving its default clip, and
  moving it into **Mashup Samples**. The steps can happen in any order, but the move is
  kept last, so **presence in Mashup Samples is what says a song is imported**. Nothing
  on disk records "warped" and nothing needs to: the move covers the whole sequence,
  because a sample sitting there unwarped or mistagged is a mistake rather than a state
  the workflow produces. Described step by step in
  [the manual workflow](../docs/the-manual-workflow.md).
- **Default clip** — the saved start position of a sample, trimmed to the bar where its
  first verse begins, so that dragging it out of the library lands it there. What makes
  an arbitrary vocal and an arbitrary instrumental line up on the first try. The audio
  before that point is not removed; only the drag-in position moves.
- **Warp marker** — one point where a sample's audio is pinned to Ableton's grid: a
  position in the audio paired with a position in beats. A sample carries an ordered list
  of them in its **sidecar**, and a **tempo** is the slope between two consecutive
  markers — no tempo is recorded as such
  ([ADR 0020](docs/adr/0020-the-sidecar-is-parseable-and-a-warp-marker-is-a-second-and-a-beat.md)).
- **Sidecar** — the `.asd` Ableton writes beside a sample. Its *presence* means only that
  Ableton analysed the file and says nothing about warping
  ([ADR 0002](docs/adr/0002-there-is-no-warped-signal.md)); its **warp markers** are what
  describe the warp.

## Tags

- **Index** — Ableton's own file database, `Live-files-<schema>.db` under
  `%LOCALAPPDATA%\Ableton\Live Database`. The authoritative tag state, and the thing the
  browser shows. Read-only to crateside, and read while Live is running
  ([ADR 0013](docs/adr/0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)).
- **Tag store** — the `Ableton Folder Info\<uuid>.xmp` Ableton keeps beside a watched
  root, holding the keywords on that root's files. A lagging, partial mirror of the index
  that can lack tags Ableton holds and keep entries for files that have gone
  ([ADR 0003](docs/adr/0003-crateside-may-write-abletons-tag-store.md)). It is what
  crateside *writes*; the index is what crateside *reads* everywhere else.
  **But writing it reconciles the index down to it**: a keyword in the index with no entry
  here is destroyed by the next write to that root, so the store is behind for reading and
  decisive for keeping (ADR 0013's 2026-09-12 amendment).
- **Written root** — a root whose tag store crateside writes, which is **Sample Staging**
  and only ever that. Read from its **tag store**, because that is where crateside's own
  writes land and the only place it can see them at once — the index takes hours to catch
  up. A file with no key there has no key.
- **Mirrored root** — a root crateside only reads, which is **Mashup Samples**. Read from
  the **index**, because its tag store is a mirror that may never converge: measured
  2026-09-10, 108 of 591 files were keyed in the index and not in the store, and the store
  had been rewritten that same day
  ([ADR 0017](docs/adr/0017-each-root-is-audited-from-the-store-that-holds-its-tags.md)).
  The two words name what the *store* is to crateside, not what the folder is for — and
  that one fact is what picks the source, so they are one axis rather than two.

## Views

- **Staging board** — the live answer to "what is in front of me right now?": every
  track in **Sample Staging**, its key and BPM, and how far through the
  human-limited steps each of its stems is. Entirely derived — it holds no state of
  its own and nothing on it is marked done by hand.
- **Partners** — the interactive answer to "what else can I use with this?".
  Returns **keys** to filter for, never files, because Ableton Live Lite admits no
  extensions and the filtering therefore happens in Ableton's own browser.
- **Queue view** — the **Queue** as work: what is in the playlist, what is already
  **fully processed**, and what is new.

## Orchestration

What crateside does when it stops only *showing* the workflow and starts *running*
it. **Evidence** and **Verdict** are keytag's terms and keep their meanings here;
the two run types below are named after which of them they produce.

- **Work order** — crateside's instruction for one track: which URL is canonical,
  where its stems belong, what quality to ask for. The thing that replaces a
  hand-written prompt. A work order says what should happen to a track next; it is
  not a record that anything did.
- **Source** — a whole file of a track's audio, as against a **stem** cut out of
  one. crateside's own download is a source; so is an official instrumental found
  later, or a local file. A track has as many as the user gives it, and each is a
  **sample** in its own right once kept. Distinct from keytag's *source*, which is
  an external reference consulted for a verdict — that sense reaches crateside only
  compounded, as **evidence source**.
- **Separation source** — the one source a track's stems are cut from, and the one
  *Swap source* changes. Every other source is an **added source**: downloaded or
  copied, tagged and staged like anything else, but never separated.
- **Detection source** — the source the **evidence run** measured. Usually the
  separation source and not necessarily: a swap moves the latter and leaves the
  former where it was. Nothing lets the user choose it.
- **Adopted sample** — a file of a track's audio that crateside took in rather than
  made, such as an instrumental the user built by hand in Ableton. A sample like any
  other, differing in one way that settles everything else: **nothing can make it
  again**, so it is never outstanding, and dropping it from the plan deletes it with
  no way back. Identified by a **label** rather than an upload, because it came from
  none — which is the whole difference between it and an **added source**. The one
  file crateside writes an audio tag on
  ([ADR 0022](docs/adr/0022-an-adopted-sample-is-a-file-crateside-did-not-make.md)).
- **Stem plan** — which files the user wants made for one track: the separator
  presets to run, whether the separation source is itself a keeper, and any
  **adopted samples** kept alongside. The user's
  own statement, stored against the track and derived from nothing. An
  **instrumental release** is not a mode but a shape of plan — no vocal preset,
  source kept.
- **Recipe** — the part of a work order that says what to make: which stems, in
  what form. A **stem plan** rendered for the separator, plus the **warp guide**,
  which is always asked for and is never part of the plan. A recipe is crateside's
  alone and is not recorded anywhere keytag reads.
- **Evidence run** — the stage that produces the **evidence** for a track:
  acquiring the audio and running the detectors. Deterministic, needs no judgement,
  and contends for nothing scarce.
- **Verdict run** — the stage that turns evidence into a **verdict**. The only
  stage that requires an agent, because weighing evidence and going to look for
  human corroboration is the only part of the workflow that genuinely needs one.
- **Handoff** — the stage that moves a track's finished stems out of the transient
  stems output and into **Sample Staging**, which is the line between the automated
  and the human halves of the workflow. It is what makes the stems output transient
  rather than merely described as such, and what lets a separated track become
  **fully processed**. Only stems with a key the user could tag with are handed
  over; the rest stay where separation put them, because staging is where tagging
  happens and there is nothing to tag them with.
- **Working set** — everything crateside is holding about one track while that
  track is in flight: the source audio it acquired, the evidence it produced
  from it, and the **acquisition**. Discarded when the track is **imported**, because
  that is the point at which there is nothing further to do with the track and
  therefore nothing further to do with what was gathered about it. Crateside's own
  scratch, never a record of anything — losing it costs a re-run, never a fact.
- **Acquisition** — how the download crateside is holding turned out, as
  `stemforge-cli` reported it: which premium outcome it met, and whether the user
  has accepted it. Kept in the working set so it dies with the audio it describes —
  a swap re-downloads, and the answer is measured again rather than inherited.
- **Premium shortfall** — a download that did not get YouTube Premium audio.
  StemForge names five ways that happens; only the two that are properties of the
  *upload* park a track, because the other two are true of every download at once
  and no per-track action would fix them (ADR 0016).
- **Parked** — crateside stopped and it is the user's turn. Not a failure and not a
  standing instruction: a finding, which the user *resolves* rather than clears.
  Two reasons, at opposite ends of the pipeline — **no key** (separated, with no
  verdict worth tagging with; the way out is a better verdict) and **premium
  shortfall** (stopped before the verdict; the way out is a better upload or the
  user's say-so).
- **Draining** — working the queue down continuously, one track at a time, for as
  long as there is anything outstanding. The normal state of the system rather than
  an action someone takes: what limits separation is that two at once are ruinous,
  not that one costs the user their machine, so there is nothing to pick a moment
  for. **Pausing** is the occasional exception, and deliberately carries no stated
  reason: whether to leave the GPU alone right now is the user's call to make, and
  a written-down justification would only be a guess at one.
- **Stage** — one of evidence run, verdict run, separation, or handoff. Stages are
  named separately because each contends for a *different* scarce resource —
  nothing, Claude quota, the GPU, and nothing again — and so each wants its own
  moment to run.
  **Independent moments, but not independent order:** separation reads facts that
  only a verdict run records, so a track becomes ready for it by having been
  judged rather than by the clock coming round.

## Flagged ambiguities

- **"Vibe"** — the existing cmdlet's label for a partner's **relationship**
  (`Perfect Match`, `Relative (Epic)`, `Energy Rise`, `Relaxed`, `Mood Shift`,
  `Modulation`). It is lossy: `Modulation` is a catch-all for every relationship
  that is not the tonic, the relative, or one step around the circle — so it covers
  musically distinct borrowings, and it is the label on nearly everything once a
  shift is involved. **Resolution: prefer "relationship" as the term**, and say
  what each actually is rather than bucketing it. Retiring `Modulation` is a
  behaviour change from the cmdlet and should be a deliberate one.
