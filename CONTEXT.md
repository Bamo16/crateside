# Context — crateside

Glossary for the helper bench that supports the **human-limited steps** of the
user's Ableton mashup workflow. Glossary only — no implementation detail.

Terms shared with the detection tool — **Track**, **Stem**, **Sample**, **Warp
guide**, **Song identity**, **Verdict**, **Source video ID**, **Queue**, **Fully
processed**, **Sample Staging**, **Mashup Samples** — are defined once in
`keytag`'s `CONTEXT.md` and are not repeated here. This file covers what is
specific to crateside: pairing, importing, tags, the views, and running the workflow.

## Places

- **Staging** — shorthand for **Sample Staging**: where samples wait to be warped.
- **The library** — shorthand for **Mashup Samples**: where a sample goes once it is
  **imported**. Ableton draws no such line: both folders sit in its *User Library* and it
  treats them alike. The distinction is crateside's, made by which folder a sample is in.
  "The library" in crateside never means Ableton's whole User Library. A more formal name is
  still open ([#66](https://github.com/Bamo16/crateside/issues/66)).

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
  kept last, so **presence in Mashup Samples is what says a song is imported**: a sample
  sitting there unwarped or mistagged is a mistake rather than a state the workflow
  produces. **Warp markers do not say a warp is done.** Ableton's *Warp* button places one
  marker at the start of the clip at the arrangement's tempo, which is right only in the
  rare case where bar 1 begins exactly there, and a hard warp (tempo changes, say) can sit
  half done in staging with many markers. The **default clip** is saved only once the warp
  is good, so it is the last step before the move. Described step by step in
  [the manual workflow](docs/the-manual-workflow.md).
- **Default clip** — the range of a sample Ableton pulls in when it is dragged out of the
  library: from where its **first verse** begins to the end of the clip. What makes an
  arbitrary vocal and an arbitrary instrumental line up at the verse on the first try;
  whether the choruses then line up depends on the verses' lengths, and is the mashup's
  problem to solve. A track with no vocals starts where a verse would sound right, and one
  with a structure too odd for that starts at beat 1 of bar 1. The audio before the start
  is not removed; only the drag-in range changes. Saved after the warp is good and before
  the move, so it is the one step that says the user finished warping.
- **Warp marker** — one point where a sample's audio is pinned to Ableton's grid: a
  position in the audio paired with a position in beats. A sample carries an ordered list
  of them in its **sidecar**, and a **tempo** is the slope between two consecutive
  markers — no tempo is recorded as such
  ([ADR 0020](docs/adr/0020-the-sidecar-is-parseable-and-a-warp-marker-is-a-second-and-a-beat.md)).
- **Sidecar** — the `.asd` Ableton writes beside a sample. Its *presence* means only that
  Ableton analysed the file and says nothing about warping
  ([ADR 0002](docs/adr/0002-there-is-no-warped-signal.md)); its **warp markers** are what
  describe the warp. Ableton writes the markers, and the **default clip**, only when *Save
  Default Clip* is pressed, so a sidecar with markers is a finished warp and one without is
  unwarped or still in progress ([note](docs/notes/2026-09-26-what-the-sidecar-records.md)).

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
- **Backing** — a file's entry in its root's **tag store**. A key held only in the **index**, with
  no entry behind it, is destroyed by the next write to that root, so backing is what makes a key
  *durable* rather than what records it. Every file arrives in **Mashup Samples** unbacked, because
  a move carries the keyword in the index and writes no entry
  ([ADR 0023](docs/adr/0023-a-key-with-no-store-entry-is-not-durable.md)).
- **Familiarity** — how likely a listener is to know a **song**: `Mainstream` or `Obscure`, under
  a tag group of crateside's own (`Familiarity|Mainstream`). Read off the reach of the
  most-followed credited artist, then the song's own, where those are clear; the user answers the
  rest, and their answer always wins
  ([ADR 0032](docs/adr/0032-familiarity-is-read-off-reach-artist-first-then-song.md)).
- **Hidden keyword** — a keyword the user took off a file by hand, which Ableton keeps in the
  file's **tag store** entry beside the keywords it still has. It records the removal itself, not
  what was there before: a tag added and taken off a minute later is hidden too. crateside never
  writes one back.
- **Genre** — what kind of music a **song** is, from a short closed list crateside owns and
  writes under a tag group of its own (`Genre|Hip Hop`). A property of the song, not the file:
  every **sample** of a song carries the same one. **Suggested** by a lookup, **settled** by the
  user, or **learned** from the user's answers for the song's primary artist
  ([ADR 0033](docs/adr/0033-a-genre-can-be-learned-from-the-users-answers-for-an-artist.md)); only
  a settled or learned genre is written. Not Ableton's built-in *Genres* group.
- **Type** — the part a **sample** plays in a mashup: **Vocal** or **Instrumental**, and
  nothing else. It names a use, not the audio: a kept full mix is Instrumental. Carried in
  Ableton's own *Type* tag group, whose built-in tags (Loop, One Shot and the rest) mean
  nothing here.
- **Written root** — a root whose **tag store** holds its whole tag state, which is
  **Sample Staging** and only ever that: every key there is one crateside wrote into the
  store itself. Read from the store, because that is the only place crateside can see its
  own writes at once — the index follows, but not at once. A file with no key there has no
  key.
- **Mirrored root** — a root whose store holds only part of its tag state, which is
  **Mashup Samples**. Read from the **index**, because the store is a mirror that may never
  converge: measured 2026-09-10, 108 of 591 files were keyed in the index and not in the
  store, and the store had been rewritten that same day
  ([ADR 0017](docs/adr/0017-each-root-is-audited-from-the-store-that-holds-its-tags.md)).
  A file moved in arrives keyed in the index and absent from the store, which is the gap
  **backing** closes.
  The two words name how complete the *store* is, not what the folder is for — and that one
  fact is what picks the source, so they are one axis rather than two. crateside writes
  both stores; which it writes stopped being the distinction when it began backing the
  library ([ADR 0023](docs/adr/0023-a-key-with-no-store-entry-is-not-durable.md)).

## Views

- **Tracks view** — every track crateside knows, whatever its state, as one list
  narrowed by filters. Its default, *Unfinished*, answers "what is in front of me
  right now?": what is in **Sample Staging**, plus the tracks still in flight or
  stopped. Entirely derived — it holds no state of its own and nothing on it is
  marked done by hand.
- **Partners** — the interactive answer to "what else can I use with this?".
  Returns **keys** to filter for, never files, because Ableton Live Lite admits no
  extensions and the filtering therefore happens in Ableton's own browser.
- **Track details** — one track on a page of its own: its key and BPM, where it is in
  the **stages**, its samples, the evidence behind its verdict, and what has happened to
  it. It offers the same actions as the track's row in the Tracks view.
- **Library** — what is finished and filed in **Mashup Samples**, counted by key and
  genre, one count per song however many samples it has, read from the **index**. It is
  also where the library's never-judged tracks are queued as the **library backfill**.

## Orchestration

What crateside does when it stops only *showing* the workflow and starts *running*
it. **Evidence** and **Verdict** are keytag's terms and keep their meanings here;
the two run types below are named after which of them they produce.

- **Work order** — crateside's instruction for one track: which URL is canonical,
  where its stems belong, what quality to ask for. The thing that replaces a
  hand-written prompt. A work order says what should happen to a track next; it is
  not a record that anything did.
- **Work lists** — crateside's own lists of tracks to work, read as what each **stage**
  still owes. A track gets on them from the **Queue** (the playlist), by URL, or from the
  Library as **library backfill**, so the Queue is one way in rather than the whole of it.
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
- **Shortfall** — what a track's **stem plan** asks for that is not on disk.
  Computed on every read and stored nowhere: the plan is a statement of want, and
  the shortfall is however much of it is still unmet. A track with no shortfall is
  complete; one with a shortfall is not, however many files it already has. Said
  bare it is always this sense — the download kind is compounded, as **premium
  shortfall**.
- **Re-cut** — cutting a track's **shortfall**, on a track that has already been
  cut once. Always asked for and never automatic: a stem the user deleted on
  purpose and one that was never made look identical on disk, so crateside states
  a shortfall and leaves the spending to the user
  ([ADR 0027](docs/adr/0027-a-shortfall-is-stated-and-never-acted-on.md)). Changing
  a **stem plan** is *not* a re-cut — it states a want and makes nothing.
- **Recipe** — the part of a work order that says what to make: which stems, in
  what form. A **stem plan** rendered for the separator, plus a **warp guide** when new
  presets are cut and the track has none yet (or a first cut that only keeps the source).
  The guide is never part of the plan. A recipe is crateside's alone and is not recorded
  anywhere keytag reads.
- **Download** — the stage that fetches a source: the audio a track is cut from, a
  kept source, or an added one. Contends for nothing scarce
  ([ADR 0019](docs/adr/0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md)).
- **Evidence run** — the stage that produces the **evidence** for a track: running
  the detectors over the audio the download acquired. Deterministic, needs no
  judgement, and contends for nothing scarce.
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
- **Tagging** — the stage that writes a track's settled key onto its files in **Sample
  Staging** as Ableton tags. Contends for nothing scarce, though it shares the **tag
  store** with a running Ableton.
- **Working set** — everything crateside is holding about one track while that
  track is in flight: the source audio it acquired, the evidence it produced
  from it, and the **acquisition**. Discarded when the track is **imported**, because
  that is the point at which there is nothing further to do with the track and
  therefore nothing further to do with what was gathered about it. A track queued only
  to be judged is never imported by crateside, so its working set goes once it is
  judged. Crateside's own
  scratch, never a record of anything — losing it costs a re-run, never a fact.
- **Acquisition** — how the download crateside is holding turned out, as
  `stemforge-cli` reported it: which premium outcome it met, and whether the user
  has accepted it. Kept in the working set so it dies with the audio it describes —
  a swap re-downloads, and the answer is measured again rather than inherited.
- **Premium shortfall** — a download that did not get YouTube Premium audio.
  StemForge names five ways that happens. Only the two that are properties of the
  *upload* park a track. The two about the account are true of every download at once,
  so no per-track action would fix them, and the fifth is a format chosen on purpose,
  which crateside never does (ADR 0016).
- **Parked** — crateside stopped and it is the user's turn. Not a failure and not a
  standing instruction: a finding, which the user *resolves* rather than clears.
  Two reasons, at opposite ends of the pipeline — **no key** (separated, with no
  verdict worth tagging with; the way out is a better verdict) and **premium
  shortfall** (stopped before the verdict; the way out is a better upload or the
  user's say-so).
- **Finding** — something crateside noticed that stops a track until the user answers
  it, shown above the track's samples: a **lookalike**, or a **premium shortfall**. A
  **parked** track is waiting on one.
- **Lookalike** — a track whose name looks like a song the ledger has already judged,
  shown as *same song?*. Judging waits for the answer: the same song carries its key and
  files across and nothing is judged or cut again; a different song is judged as usual,
  and the question is not asked again for that pair (#41).
- **Gave up** — a **stage** failed too often to try again: five times for a download or
  an evidence run, three for the rest. A download YouTube will never serve (unavailable,
  private, removed, blocked) gives up at the first failure, and says why (#63). Nothing
  automatic moves the track after that; *Retry* puts it back in line.
- **Work this first** — the one track the user wants worked next. Download, detection,
  judging and separation take it before anything else they are due to pick, and asking
  for another replaces it. Held in
  memory, so a restart forgets it (ADR 0015). The one way a **library backfill** track is
  judged outside an **idle window**.
- **Draining** — working the **work lists** down continuously, one track at a time,
  for as long as there is anything outstanding. The normal state of the system rather than
  an action someone takes: what limits separation is that two at once are ruinous,
  not that one costs the user their machine, so there is nothing to pick a moment
  for. **Pausing** is the occasional exception, for separation or judging, and
  deliberately carries no stated reason: whether to leave the GPU or the Claude quota
  alone right now is the user's call to make, and a written-down justification would
  only be a guess at one. A pause survives a restart.
- **Stage** — one of download, evidence run, verdict run, separation, handoff, or
  tagging. Stages are named separately because they contend for different resources —
  only the verdict run spends Claude quota and only separation the GPU; the rest
  contend for nothing scarce — and so each wants its own moment to run.
  **Independent moments, but not independent order:** separation reads facts that
  only a verdict run records, so a track becomes ready for it by having been
  judged rather than by the clock coming round.
- **Library backfill** — library tracks tagged by hand before crateside, queued
  from the Library page to be judged and never cut. Marked when queued, so a track
  stays backfill after its download makes it stop being library-only. Judged only in
  an **idle window**, never by the ordinary verdict stage, unless the user asks for
  one first ([ADR 0035](docs/adr/0035-the-library-backfill-is-judged-only-in-idle-windows.md)).
- **Idle window** — a five-hour Claude window that has read 0% for fifteen minutes.
  One verdict spends it, then none for five hours
  ([ADR 0029](docs/adr/0029-an-idle-window-is-opened-by-one-verdict.md)).

## Flagged ambiguities

- **"Vibe"** — the existing cmdlet's label for a partner's **relationship**
  (`Perfect Match`, `Relative (Epic)`, `Energy Rise`, `Relaxed`, `Mood Shift`,
  `Modulation`). It is lossy: `Modulation` is a catch-all for every relationship
  that is not the tonic, the relative, or one step around the circle — so it covers
  musically distinct borrowings, and it is the label on nearly everything once a
  shift is involved. **Resolution: prefer "relationship" as the term**, and say
  what each actually is rather than bucketing it. Retiring `Modulation` is a
  behaviour change from the cmdlet and should be a deliberate one.
