# Context — crateside

Glossary for the helper bench that supports the **human-limited steps** of the
user's Ableton mashup workflow. Glossary only — no implementation detail.

This file covers what crateside is about: the units it works on, keys, pairing, importing, tags,
the views, and running the workflow.

## Units and intake

- **Track** (or **Song**) — one piece of source material: an artist's recording, the thing a
  key verdict is about.
- **Stem** — one separated part of a track (vocal, instrumental, drums). Describes only **how a
  file was made**, and implies nothing about what it is for or whether it is kept.
- **Sample** — a file the user actually mixes with: key-tagged, and destined for **the
  library**. Describes **what a file is for**. Usually a stem, but for a release that is already
  instrumental the source track itself is the sample. The two axes cross, which is why both
  words are needed: a vocal stem and a drums stem are equally stems and only one is kept, while an
  instrumental release is a sample and no kind of stem at all.
- **Canonical version** — of the several uploads a song has, the one that is the right master.
  Chosen once, at the front of the workflow; the verdict and every stem inherit it. Its address
  is the canonical URL.
- **Instrumental release** — a track with no vocals to begin with, so nothing is separated out
  and **the source track is the sample**. The user states it (`has_vocals: false`); crateside
  never concludes it. Named around the word because "instrumental" already names a stem preset.
- **Human-limited steps** — the parts of the workflow only the user can do: **warping**,
  **tagging**, and sometimes renaming. Everything before them crateside automates; nothing after
  them is.
- **Song identity** — how a song is keyed in the ledger: lowercased `artist - title` with
  whitespace collapsed and any stem suffix like `(Instrumental - Full)` dropped, since key is
  song-level. Always derived from the artist and title beside it, never authored, and computed
  only from names already repaired of encoding damage. It says *which song*, never *how a run
  was done*: a method note belongs in the reasoning, not appended to the identity.
- **Track key** — the one string a track is worked by: its YouTube video ID, or `<source>~<id>` for
  a track that is not on YouTube (`soundcloud~…`, `file~` and the start of the file's SHA-256).
  Stems of such a track carry `source: track:<key>`. `TrackKeys` is the one parser (ADR 0077).
- **Import** — the audio of a track that is not on YouTube, kept under `imports\<key>` and never
  pruned. It is the track's separation source.
- **Recipe** — a named plan a track is taken in with: Standard (vocal, instrumental, drum guide)
  or Instrumental (the source and the drum guide). Settings chooses the default; an inbox
  subfolder named for one gives its files that recipe (ADR 0077).
- **Release** — what a track's source says about the release it came from: album, album artist,
  track and disc numbers, genre, composer, organization, a year that says whether it is a release
  year or an upload date, and the cover.
- **Inbox** — the folder crateside consumes: audio files and zips saved there become tracks.
- **Source video ID** — the YouTube video identifier a YouTube track came from, embedded in each stem
  file at separation time and so surviving renames. The durable anchor from a stem on disk back
  to its **song identity**. A song may have several; all keep resolving (see **Restatement**).
- **Queue** — the YouTube playlist the user adds tracks to as they think of them. Unordered and
  open-ended: being in it means "I want this eventually", never "process this next".
- **Fully processed** — a track has genuinely made it out of the **Queue**: a verdict recorded
  for its **source video ID**, *and* at least one stem carrying that ID on disk. Both halves are
  required, since a key can be determined for a track whose separation then failed.
- **Stems output** — where separation writes a track's stems (`Music\Stems`, by whether the
  track has a key to tag with). Transient: the **handoff** moves them on.
- **Dummy project** — a throwaway Ableton project the user drags staged samples into, to **warp**
  them and apply **Key tags** by hand.

## Keys

- **Verdict** — a confidence-rated conclusion about a *song*'s key, made by the judge, by
  crateside's rules or by the person (**decided by**). It names a
  **tonal center**, and where the song moves, its **sections**; it may be **mixed**. A song-level
  fact: one verdict covers every stem of the song. Its tag is derived from it by the **collapse**
  and is never the verdict itself.
- **Decided by** — who made a verdict: the **judge** (an agent session), **rules** (crateside
  rules on a key by itself when two detectors and every page agree, or all three detectors
  agree and no page states a key) or the **person** (chosen on the song page, recorded HIGH and
  shown as "your call"). With no judge, a track the rules leave open waits for the person
  ([ADR 0076](docs/adr/0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md)).
- **Tonal center** — a tonic and a **diatonic mode**: *A Dorian*, *E minor*. What the music
  is, as a person transcribing it would name it.
- **Diatonic mode** — one of the seven rotations of the major scale. A closed set; harmonic and
  melodic minor are color within Aeolian, not modes of their own. Only a human source can
  establish one, since every detector reports major or minor alone.

  | mode | quality |
  |---|---|
  | Ionian (major), Lydian, Mixolydian | major |
  | Aeolian (minor), Dorian, Phrygian, Locrian | minor |

- **Section** — a part of a song with its own tonal center: *A Dorian (verse), E minor
  (chorus)*. A source's key covers the sections it analyzed, not necessarily the song. The judge gives each a start time in seconds; older verdicts have none.
- **Mixed** — two modes on one tonic at once, such as a major melody over minor harmony. Its
  collapse follows the harmony.
- **Collapse** — reducing a tonal center to one of the 24 major and minor keys Ableton's Key
  tag admits, keeping the tonic: *A Dorian* → *A minor*, *G Mixolydian* → *G major*. Always
  shown beside the mode, never instead of it. The key with the same notes (*A Dorian* has
  *G major*'s) is next to the collapsed one on the **Camelot** wheel only for Aeolian, Lydian and
  Mixolydian; for Dorian and Phrygian it is the relative major's neighbor (*A Dorian*: 8A → 9B;
  *A Phrygian*: 8A → 7B), and for Locrian further off (*A Locrian*: 8A → 6B).
- **Backer** — another detector that names the same key as keydetect. Whether keydetect has
  backers says more about its being right than its own confidence does.
- **Window** — a stretch of a track detected on its own, given by the user in bars or asked
  for by the judge. The windows of a judged run become its verdict's **sections**.
- **Bar number** — a bar as Live's ruler numbers it for a warped clip: the warp's beat 0 is bar 1,
  the bars before it are -1, -2… (there is no bar 0), and the bar length is the warp's time
  signature. The **default clip**'s start is a point on that ruler, not its bar 1
  ([ADR 0072](docs/adr/0072-bars-are-numbered-as-lives-ruler-numbers-them.md)).
- **Direction** — the user's free-text note to the judge, sent with windows and kept with
  the run.
- **Ear** — the user's own reading of a key, a source of its own kind. A hint by default;
  the Ear setting can count it as a human source.
- **Evidence** — everything gathered about a song that bears on its key: each detector's answer
  and confidence, the chord progression, the in-stream key flicker, and the **evidence
  sources** consulted. Facts, not conclusions; the **verdict** is what the judge concludes from them.
- **Evidence source** — an external reference consulted for a verdict (a Hooktheory page, a
  photographed sheet): where it came from, what key it stated, and its **source kind**. Weighed
  above the judge's own interpretation.
- **Source kind** — what an evidence source is worth on the tonic, a closed set:
  **authoritative** (human analysis, published sheet music), **corroborating** (human
  transcription), **chord recognizer** (its chords are evidence, its key never is) and
  **algorithmic** (shares the detectors' failure modes, so corroboration only). A kind, not a rank.
- **Confidence** — HIGH, MEDIUM or LOW on a verdict, from detector agreement plus corroboration
  by human sources. Governs whether a tag is recommended.
- **Tonic check** — the argument, against the chord progression and key flicker, for why the
  verdict's tonic is the tonic and not its relative. Owed on every run, and recorded apart from
  the reasoning because it is read against the chords it argues about. It says what the music
  does; the **reasoning** says what the evidence is worth (ADR 0064).
- **Candidate** — a key the judge weighed besides the verdict's: one it turned down, or for a
  WITHHELD, one still open. Shown on Track details as *Considered*.
- **First pass** — the judge session that asked for windows by withholding. Its reasoning is kept
  once on the run that follows, and Track details shows it folded as *First pass: withheld*.
- **Supersede** — how a re-run replaces an earlier verdict: filed under the same **song
  identity**, the latest run wins, so each run's prose stands on its own.
- **Restatement** — the ledger's record that another upload is a song already judged. It
  carries the song's verdict to the new upload and never changes it.

## Places

- **Staging** — where samples wait to be warped, once called *Sample Staging*. The holding area
  between the automated and the human halves of the workflow: what is in it has been through
  separation but not the **human-limited steps**, and it may sit there for weeks. It is not a
  queue and implies no urgency. The folder is a setting, not a fixed place.
- **The library** — where a sample goes once it is **imported**, once called *Mashup Samples*;
  a sample there is warped and tagged. Ableton draws no such line: both folders sit in its *User Library* and it treats them
  alike. The distinction is crateside's, made by which folder a sample is in. "The library"
  in crateside never means Ableton's whole User Library. The two names were settled in
  [#66](https://github.com/Bamo16/crateside/issues/66); the folders are changed in Settings
  and take effect at the next start.

## Pairing

The domain of "what else can I play over this?". The **source** is the tonal center of
the thing the user already has in hand: its verdict's primary center, a section's, or one picked
by hand; everything below describes candidates against it.

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
  wrapped so it never exceeds 6. Only breaks ties in the ranking, after shift and notes
  shared.
- **Notes shared** — how many of the source's seven notes a partner's scale holds once moved
  by its **shift**: `6 of 7`. The second sort key, after shift
  ([ADR 0056](docs/adr/0056-partners-pairs-by-notes.md)).
- **Relationship** — what a partner *is* to the source, named only where it is one of two:
  **same key**, or **same notes** (a **same-notes key**, which for plain major and minor is the
  **relative**). Every other partner is described by its notes shared.
- **Same-notes key** — a major or minor key whose seven notes are a tonal center's, other than
  its collapse: *G major* and *E minor* for *A Dorian*. For Ionian and Aeolian it is the
  **relative**, and is called that. Shared with keydetect-harness.
- **Relative** — the collapse's major or minor with the same notes: *C major* for *A minor*,
  and also for *A Dorian*, whose notes it does not share.
- **Parallel** — the collapse's tonic with the other quality: *G minor* for *G major*, *A major*
  for *A Dorian*. Shared with keydetect-harness.
- **Camelot code** — the harmonic-mixing notation (`8A`, `5B`, …) that numbers the
  circle of fifths and letters the mode. An alternate spelling of a key, not a
  separate concept.

## Import

- **Import** — finishing a song: warping it, tagging it, saving its default clip, and
  moving it into **the library**. The steps can happen in any order, but the move is
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
  of them in its **analysis file**, and a **tempo** is the slope between two consecutive
  markers — no tempo is recorded as such
  ([ADR 0020](docs/adr/0020-the-sidecar-is-parseable-and-a-warp-marker-is-a-second-and-a-beat.md)).
- **Warped BPM** — the tempo a finished warp holds: the stable tempo covering the most bars of
  the default clip, or the median bar where no tempo holds for eight
  ([ADR 0038](docs/adr/0038-a-warps-bpm-is-the-stable-tempo-that-holds-longest.md)). Shown in
  place of the ledger's estimate once a track is warped.
- **Tempo** — beats per minute of the beat the user warps to; the number is the **BPM**. A
  reading at half or double is the same tempo at **the other octave**, and the octave is the
  user's call, not an error.
- **Headline tempo** — the one BPM shown for a track everywhere: the **warped BPM** where there is
  one, else crateside's reading, else the **ledger's estimate** (the BPM of imported history, a warp
  starting point that no new run writes)
  ([ADR 0049](docs/adr/0049-a-bpm-shows-only-the-precision-crateside-can-vouch-for.md)). A track
  with no stems yet shows the reading of its source marked **from the full mix**, approximate
  and for display only ([ADR 0080](docs/adr/0080-a-track-shows-a-tempo-from-its-source-before-it-has-stems.md)).
- **Vouched** — a BPM known to the hundredth: a steady warp whose grid holds against the track's
  transients (`GridCheck`: no step, no drift beyond the noise), or a reading whose 30 s windows
  agree within 0.02%. Written bare (`128`, `119.50`); anything else is **approximate** and
  written `~128`. Nothing is snapped to a round number
  ([ADR 0050](docs/adr/0050-a-warp-shows-as-itself-and-is-vouched-for-by-its-grid.md)).
- **Single-marker warp** — a warp whose one user marker, on a hit at its right place on the
  grid, fixes every beat, because the track holds one tempo. The analysis file stores it as two
  markers (Ableton adds one 1/32 beat on).
- **Steady** — a track at one constant tempo throughout: programmed, or played to a click.
- **Drifting** — a track written at one tempo but performed without a click, so its timing
  moves with the performance. Two kinds so far: **wandering**, where the average tempo moves
  over the song, and **looped**, where a human-played loop repeats and the same hits land off a
  straight grid every time while the average tempo holds.
- **Tempo change** — an intended change: a ritardando, an accelerando, or a section at a new
  tempo. Rare; the user warps these by hand and wants no automation for them.
- **Warp class** — *Steady*, *Mostly steady* or *Drifting*: what crateside reads mechanically
  from a warp's markers bar by bar (ADR 0038). *Steady*: one tempo across the clip. *Mostly
  steady*: a main tempo, plus a drifting stretch or a **tempo change**. *Drifting*: no eight bars
  hold one tempo, which is nearly always a **drifting** track, but a long accelerando would land
  there too. A **looped** track is *Drifting* by its markers, while a detector reading its average
  finds it steady.
- **Marker error** — how far a user's warp marker sits from the true beat: most within
  5–10 ms, a few up to ~25 ms, by the user's estimate.
- **Alignment error** — how far a detector's beat grid lands from a reference beat grid, in ms;
  positive is late. An **off-beat lock** is a grid about half a beat off, on the "and".
- **Analysis file** — the `.asd` Ableton writes beside a sample; informally the **sidecar**,
  and `AnalysisFile` in code. Its *presence* means only that
  Ableton analyzed the file and says nothing about warping
  ([ADR 0002](docs/adr/0002-there-is-no-warped-signal.md)); its **warp markers** are what
  describe the warp. Ableton writes the markers, and the **default clip**, only when *Save
  Default Clip* is pressed, so an analysis file with markers is a finished warp and one without is
  unwarped or still in progress ([ADR 0037](docs/adr/0037-a-saved-default-clip-is-a-finished-warp.md)).
- **Borrowed warp** — a finished warp crateside copied onto a stem from a sibling cut from the
  same upload, with its default clip saved: a cut of the stem's own kind if there is one, else an
  instrumental, karaoke or the kept source. It counts as finished, and stays *borrowed* until Live
  saves over the file
  ([ADR 0043](docs/adr/0043-a-borrowed-warp-is-finished-and-says-where-it-came-from.md),
  [ADR 0045](docs/adr/0045-a-recut-takes-its-warp-from-its-own-kind-first.md)).
- **Pinned build** — for each of beatdetect and keydetect, the one build of those crateside knows
  for that filter (#102) that makes new readings; the ffmpeg on PATH is not involved. A beatdetect
  reading keeps the build that made it, and one from an older build than the pin stays in use until
  the beat worker has read the file again, after every file with no reading. A judged song's
  evidence that another keydetect build read is read again, all three detectors, when the evidence
  stage has nothing else to do; its verdict is left as it was
  ([ADR 0066](docs/adr/0066-keydetect-has-its-own-pinned-build.md)).
  Before a song is judged its working-set evidence is made whole the same way, so the judge never
  sees fewer detectors than exist ([ADR 0068](docs/adr/0068-a-song-is-judged-only-on-complete-evidence.md)).
- **Starter warp** — a one-marker warp crateside writes, at its own steady beatdetect reading of the
  instrumental, onto each stem of an upload nothing is warped on yet as it lands in staging, so the
  group dragged into an empty set sets its tempo. It is written only where that one tempo keeps the
  beats on its grid through the track, within 10 ms
  ([ADR 0048](docs/adr/0048-stems-arrive-in-staging-already-warped.md)). Its marker sits on the first beat the loud
  transients place, numbered within its bar from beatdetect's downbeat where the filter is sure of
  it, and its clip starts on the bar line at or before the marker
  ([ADR 0054](docs/adr/0054-a-starter-warps-marker-sits-on-a-beat-the-loud-transients-place.md)). It is not
  a finished warp: the stem still waits to be warped, it never lends, a lent warp replaces it, and
  it becomes the user's warp once Live saves over it
  ([ADR 0047](docs/adr/0047-a-starter-warp-and-where-transients-came-from.md),
  [ADR 0048](docs/adr/0048-stems-arrive-in-staging-already-warped.md)).
- **Warp guide** — a **role** a sample plays, not a kind of file: a track's drums stem, cut to be
  looked at while warping rather than heard. It is never tagged and never mixed, and deleting it
  is a step in the workflow, so its presence says "warping outstanding". A drums stem could be
  kept; every one is treated as a guide, and keeping one is done by hand.
  Once the rest of the track is in **the library**, crateside keeps it, with its analysis file,
  in `Music\Stems\Drums`, outside every folder Ableton browses
  ([ADR 0046](docs/adr/0046-a-filed-tracks-warp-guide-is-kept-outside-ableton.md)).
- **Ready to move** — a track whose staged samples all have their default clip saved: warped, with
  only the move to **the library** left.
- **Clip color** — the color a staged stem's clip takes when dragged into a set, saved in its
  analysis file: its kind picks the family (drums, vocals, instrumentals) and its song the shade.
  It says nothing about the warp, which is the stem's **Collection**'s to say. crateside sets it on
  every saved analysis file in staging, the user's included
  ([ADR 0058](docs/adr/0058-a-staged-stem-shows-its-state-in-live.md)).
- **Clip gain** — the gain a sample's default clip carries, saved in its analysis file, that brings
  the whole file's integrated loudness to the **loudness target** (-14 LUFS, a setting). Drums,
  and a stem quieter than the floor (-24 LUFS), keep 0 dB. crateside sets it on every saved
  analysis file in staging and the library, the user's included, and sets it again whenever Live
  saves over one; the audio is never touched
  ([ADR 0059](docs/adr/0059-a-sample-arrives-at-one-loudness.md)).
- **Warp off clip** — the analysis file crateside writes for a staged stem that gets no **starter
  warp** (one tempo does not hold on its beats, or its tempo is not read yet): a saved clip over the
  whole file with Warp off, carrying its **clip color** and **clip gain** and the pair of markers
  Live writes at a clip's start, without which Live crashes
  ([ADR 0091](docs/adr/0091-a-saved-clip-with-warp-off-carries-the-marker-pair-live-writes.md)).
  It is not a warp:
  the stem still waits to be warped by hand, it never lends, a starter whose tempo holds or a lent
  warp replaces it, and it becomes the user's once Live saves over it
  ([ADR 0059](docs/adr/0059-a-sample-arrives-at-one-loudness.md),
  [ADR 0048](docs/adr/0048-stems-arrive-in-staging-already-warped.md)).

## Tags

- **Collection** — one of the seven colored labels in Live's browser, numbered 1 to 7 and named
  by the user. crateside puts each staged stem in the one for its warp state (*Check beat*,
  *Warped*, *Warp by hand*), taken away on import, and every stem of a track whose Key tag
  may mislead, or that the user marked as changing key, in *Key caveat*. *Mine* is the user's.
  *Odd bars* is every stem of a track the user marked as not consistent 4/4
  ([ADR 0058](docs/adr/0058-a-staged-stem-shows-its-state-in-live.md)).
- **Meter** — the `Meter|` tag group the user sets in Live: a base (`4/4`, `3/4`, any `n/d`, or
  `Mixed`) and optionally `Odd bars`. Any Meter tag means the user heard the track through and
  signed off its meter; none means unchecked. crateside copies a change on one stem to the track's
  others and shows *Odd bars* for anything but plain 4/4
  ([ADR 0092](docs/adr/0092-a-meter-tag-set-in-live-records-that-the-track-was-heard-through.md)).
- **Track mark** — a yes/no the user sets on a track in crateside (*Changes key*, *Odd bars*), kept
  in the ledger's `TrackMarks` table by song ([ADR 0082](docs/adr/0082-the-persons-decisions-live-in-the-ledger-database.md)) and never read from Ableton. It records no place and no amount, and
  "not marked" means "not noticed yet". Live shows it as the matching **Collection**.
- **Not fit for measuring** — a mark on a track, or on stretches of it, that the harnesses leave out
  when scoring detectors, each with a reason. Stretches are typed in Live's bars with a warp and in
  `m:ss` without, and kept in seconds of the source. In the ledger's `MeasuringExclusions` table by
  song, and served at `/api/measuring-exclusions`
  ([ADR 0085](docs/adr/0085-a-track-or-a-stretch-of-it-can-be-marked-unfit-for-measuring-detectors.md)).
  It is not a **Track mark** and not a Collection.
- **Page choice** — the person's correction to a track's lookup, on the song page: *Not this song* on
  a counted page, *Count this page* on a title-only candidate, or a Hooktheory or Musicnotes page
  *added* by address. Kept in the ledger's `PageChoices` table by track, apart from the lookup's own result; the
  rules and the judge read the **effective pages** (the lookup's, with the choices applied)
  ([ADR 0078](docs/adr/0078-the-person-corrects-which-pages-count-for-a-song.md)).
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
  *durable* rather than what records it. Every file arrives in **the library** unbacked, because
  a move carries the keyword in the index and writes no entry
  ([ADR 0023](docs/adr/0023-a-key-with-no-store-entry-is-not-durable.md)).
- **Familiarity** — how likely the audience is to recognise a **song**: `Mainstream`, `Niche`
  (known within a scene, not generally) or `Obscure`, under a tag group of crateside's own
  (`Familiarity|Niche`). Two votes read off the song's own signals, never the artist's: some
  recording of it has 3,500 or more distinct ListenBrainz listeners, and its own English Wikipedia
  article had 20,000 or more views by people in the last twelve months. Both is `Mainstream`,
  neither `Obscure`, one `Niche`; a song with no recording, or no article, votes no. Every song with
  an artist and a title gets a value. The person corrects it, song by song
  ([ADR 0089](docs/adr/0089-familiarity-is-read-off-the-songs-own-listeners-and-its-wikipedia-article.md)).
- **Hidden keyword** — a keyword the user took off a file by hand, which Ableton keeps in the
  file's **tag store** entry beside the keywords it still has. It records the removal itself, not
  what was there before: a tag added and taken off a minute later is hidden too. crateside never
  writes one back.
- **Genre** — what kind of music a **song** is: up to two of the 29 tags of Ableton's own *Genres*
  group, the ones most of the song's **styles** map onto through a table shipped with crateside,
  plus any the person adds of their own (`Genres|Meme`). A property of the song, not the file:
  every **sample** of a song carries the same ones. The person's choice among the 29 replaces
  crateside's; one of their own is added beside it. Learned from the person's answers for the song's
  primary artist where the styles map onto nothing
  ([ADR 0088](docs/adr/0088-genre-is-abletons-own-genres-filled-from-the-sources.md)). A genre added
  in Live to a **sample** crateside did not write is the person's answer for the song; one crateside
  wrote and the person took off is not assigned again
  ([ADR 0087](docs/adr/0087-crateside-tags-every-song-and-the-person-corrects-in-crateside-or-in-live.md)).
- **Style** — a source's label for a song (`boom bap`, `Dance`), every one kept as the sources gave
  it (MusicBrainz recording, MusicBrainz artist, Deezer, iTunes), shown in crateside and never
  written as a tag. The table that maps them onto genres lists the ones it does not map on the
  Library page.
- **Type** — the part a **sample** plays in a mashup: **Vocal** or **Instrumental**, and
  nothing else. It names a use, not the audio: a kept full mix is Instrumental. Carried in
  Ableton's own *Type* tag group, whose built-in tags (Loop, One Shot and the rest) mean
  nothing here.
- **Lead** / **Sparse** — whether a vocal stem can carry a mashup as its vocal, or is kept for
  chops: `Type|Vocal|Lead` or `Type|Vocal|Sparse`. A property of the stem file, measured from its
  audio ([ADR 0090](docs/adr/0090-a-vocal-stem-is-lead-or-sparse-measured-from-its-audio.md)).
- **Written root** — a root whose **tag store** holds its whole tag state, which is
  **Staging** and only ever that: every key there is one crateside wrote into the
  store itself. Read from the store, because that is the only place crateside can see its
  own writes at once — the index follows, but not at once. A file with no key there has no
  key.
- **Mirrored root** — a root whose store holds only part of its tag state, which is
  **the library**. Read from the **index**, because the store is a mirror that may never
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
  right now?": what is in **Staging**, plus the tracks still in flight or
  stopped. Entirely derived — it holds no state of its own and nothing on it is
  marked done by hand.
- **Cover** — the picture StemForge embeds in a track's audio, shown beside the track on Tracks, the
  song page and the dashboard. It is taken from the track's own file (a sample in Staging or the
  library, else the working set's source) the first time it is asked for, kept under
  `%LOCALAPPDATA%\crateside\art\` until that file changes, and served at
  `/art/{trackKey}?size=small|full`. A track with no picture gets a plain grey tile.
- **Partners** — the interactive answer to "what else can I use with this?".
  Returns **keys** to filter for, never files, because Ableton Live Lite admits no
  extensions and the filtering therefore happens in Ableton's own browser.
- **Track details** — one track on a page of its own: its key and BPM, where it is in
  the **stages**, its samples, the evidence behind its verdict, and what has happened to
  it. It offers the same actions as the track's row in the Tracks view.
- **Library** — what is finished and filed in **the library**, counted by key and
  genre, one count per song however many samples it has, read from the **index**. It is
  also where the library's never-judged tracks are queued as the **library backfill**.

## Orchestration

What crateside does when it stops only *showing* the workflow and starts *running*
it. The two run types below are named after which of **evidence** and **verdict** they produce.

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
  **sample** in its own right once kept. Distinct from an **evidence
  source**, an external reference consulted for a verdict.
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
  in the ledger.
- **Download** — the stage that fetches a source: the audio a track is cut from, a
  kept source, or an added one. Contends for nothing scarce
  ([ADR 0019](docs/adr/0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md)).
- **Evidence run** — the stage that produces the **evidence** for a track: running
  the detectors over the audio the download acquired. Deterministic, needs no
  judgment, and contends for nothing scarce.
- **Verdict run** — the stage that turns evidence into a **verdict**. The only
  stage that requires an agent, because weighing evidence and going to look for
  human corroboration is the only part of the workflow that genuinely needs one.
- **Verdict file** — what a verdict run's session writes, and the only thing it writes: the
  verdict, its sources and its reasoning, or a WITHHELD asking for **windows**. crateside
  checks it and records it in the ledger ([docs/judge.md](docs/judge.md)). Besides the ledger's
  rules it must name a key the evidence holds, and a HIGH in it must be backed, or it is resumed
  once and then refused or capped at MEDIUM
  ([ADR 0069](docs/adr/0069-a-verdict-is-checked-against-its-evidence-before-it-is-recorded.md)).
- **Handoff** — the stage that moves a track's finished stems out of the transient
  stems output and into **Staging**, which is the line between the automated
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
  judged. A track that was imported keeps its source for the **source retention** the user set,
  counted from when its last stem left staging, and *Forever* keeps it. Crateside's own
  scratch, never a record of anything — losing it costs a re-run, never a fact.
- **Source retention** — how long a track's source download stays in its **working set** after
  the last stem of the track leaves staging for the library: a number of days, or *Forever*.
  Zero days discards at once. Where every source is: `source-index.json`
  ([ADR 0061](docs/adr/0061-a-source-download-is-kept-for-a-set-time-after-its-stems-reach-the-library.md)).
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
  judged outside the **spend-down**.
- **Weekly hold** — the verdict stage not starting a track while weekly Claude use is at or
  above 85%. Not the user's pause: it is not remembered, and lifts by itself when the figure
  drops or the weekly reset passes
  ([ADR 0053](docs/adr/0053-judging-stops-at-85-percent-of-the-weekly-limit.md)). The
  **spend-down** has its own ceiling instead. A judge on another endpoint is never held, and
  with Library backfill on it works the backlog in ordinary judging
  ([ADR 0067](docs/adr/0067-a-judge-off-the-claude-limit-is-not-held-by-it.md)).
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
  from the Dashboard's library section to be judged and never cut. Marked when queued, so a track
  stays backfill after its download makes it stop being library-only. Judged only in
  the **spend-down**, never by the ordinary verdict stage, unless the user asks for
  one first ([ADR 0035](docs/adr/0035-the-library-backfill-is-judged-only-in-idle-windows.md)).
  On screen the **spend-down**'s switch, ceilings and states are all called *Library backfill*
  (Settings, the Dashboard, a track's *Next*); *spend-down* is the code's and the docs' name for
  the weekly window it runs in.
- **Sourceless song** — a library song StemForge cut before it recorded a source (about
  June 2026), so no video ID names it. Its row is library-only and keyed `name:` plus the
  file name without the preset, the identity the library counts already use; renaming the
  files changes the key, and the song page follows. It has no upload, so nothing that needs
  one is offered, and it is never queued for backfill (#94).
- **Spend-down** — the hours before the weekly Claude reset (three by default), in which the
  backlog is judged track after track until weekly use reaches its ceiling (100% by default, so every drop that would expire at the reset is spent),
  pausing while the five-hour window is near its limit. Re-judges go before the **library
  backfill** ([ADR 0063](docs/adr/0063-the-backlog-is-judged-in-a-weekly-spend-down.md)).
  _Avoid_ "idle window": the roll it named is gone.

## Flagged ambiguities

- **"Vibe"** — the cmdlet's labels for a partner (`Perfect Match`, `Relative (Epic)`,
  `Energy Rise`, `Relaxed`, `Mood Shift`, `Modulation`), retired by ADR 0056. Say **same key**,
  **same notes** or **notes shared**. _Avoid_ "modulation" for a partner: since ADR 0055 it
  reads as a key change within a song, which is a **section**.
- **"Stable tempo"** — ADR 0038's word for a run of 8+ bars at one tempo, not the opposite of
  **drifting**. The warp classes it named *Steady with changes* and *Unsteady* were renamed
  *Mostly steady* and *Drifting* on 2026-09-28; notes and design handoffs before then use the old
  names.
