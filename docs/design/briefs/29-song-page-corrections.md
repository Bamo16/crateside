# crateside: correct genre, familiarity and Lead/Sparse on the song page, and show where each came from

Issue: #193 (a song-page correction for genre, familiarity and Lead/Sparse)

For a Claude Design session (`docs/agents/design.md`, ADR 0057), then a build agent; Bamo reviews first.
Read `docs/design/DESIGN.md` (the Tags group, around lines 440-500) and the canvas
`docs/design/canvases/Crateside Track Page.dc.html` first, and `docs/design/handoffs/20-track-page.md`.
ADRs touched: 0082 (the person's decisions live in the ledger database), 0087 (the person corrects in
crateside or in Live), 0088 (genre), 0089 (familiarity), 0090 (Lead/Sparse), 0077 (recipes; stems are
per preset). Related: #194 (measure Lead/Sparse on hand-tagged stems), #195 (tag every vocal stem from
the accepted cuts), #196 (re-measure familiarity cuts). #190, #191 and #192 are landed.

## Problem

Bamo can set genre and familiarity from the strips, but cannot see why a value is what it is, cannot
remove a genre or add one of his own from the page, and has no page at all for Lead/Sparse. He wants
one place on the song page, with no queue and no prompt.

## Current behavior

- **Two strips exist** and share `Components/ChoiceStrip.razor`: `GenreStrip.razor` and
  `FamiliarityStrip.razor`, both mounted in `SideStack.razor:68-69` (the `.group.tags` div), used by
  `Song.razor:235` and `Tracks.razor:521`. `GenreStrip Shape=Cell` is the Tracks column
  (`Tracks.razor:496`).
- **Origin is partly shown already.** `GenreStrip.Note(record)` yields "your call", "from your answers
  for {artist}", "you removed it", "looked up: styles ...", "not looked up yet".
  `FamiliarityStrip.Note` shows listeners, Wikipedia views and "your call".
- **Genre writes** go through `GenreStore` (`Genres/GenreStore.cs`): `Choose` (:44; one of the 29
  replaces the person's previous one, a name of his own is added), `Reject(song, genre, removal)`
  (:75; **requires a Live removal id**), `Learn` (#61). `GenreRecord` (:269) holds Heard, Mine, Learned,
  LearnedFrom, Tagged, Rejected, RemovalsSeen; `GenreVocabulary.IsGenre` (:49) and `IsName` (:53) check
  a name.
- **Familiarity** is `FamiliarityStore` (`Familiarity/FamiliarityStore.cs`): `Measure`, `Settle`,
  `Reject`; `FamiliarityRecord` (:216) holds Listeners, Pageviews, WikidataItem, Settled, Suggested.
- **Lead/Sparse** is `VocalAnswerStore` (`Vocals/VocalAnswerStore.cs`): `Read`, `Settle(stem, value)`,
  `Clear(stem)`, keyed by stem identity (track plus preset). Nothing on the song page shows it.
- **Page and Live answers are indistinguishable.** `LiveAnswers.Record`
  (`Orchestration/Handoff/LiveAnswers.cs:44`) calls the same `Choose`, `Settle` and `Reject`, and no
  row says who set it. "Set in Live" therefore needs a marker the stores do not have.
- **Measured has no source yet.** #194 and #195 produce the Lead/Sparse measurement; until then
  there is none to show. Familiarity's measured side (listeners, pageviews) exists.

## What the page needs

1. **Genre:** crateside's one or two of the 29, the person's own, styles read-only; replace one of
   the 29, add a name of his own (`GenreVocabulary.IsName`), remove any.
2. **Familiarity:** the value with ListenBrainz listeners and Wikipedia yearly views (or "no article")
   as structured numbers, and a way to set another value.
3. **Lead/Sparse per vocal stem**, once measured: the value and an override, one line per vocal stem.
4. **Origin on every value**, one of: *looked up*, *learned from your answers for {artist}*, *measured*,
   *your call*, *you removed it*, *set in Live*.

## Proposal

- **Place:** extend the existing `.group.tags` in `SideStack` (not a new page or modal). The strips
  keep their shape; each gains a one-line origin and a details row (`<details>`, closed by default,
  as the page already uses) holding the numbers, styles and the remove/add controls. The Design
  session decides the visuals; the constraints are one group, no queue, nothing that appears unasked.
- **Origin marker:** a nullable `Origin` column (`Page` or `Live`) on the genre, familiarity and
  vocal answer rows, one EF migration after `20261110212347_LiveAnswers`, set by the caller
  (`LiveAnswers.Record` writes `Live`; the page writes `Page`); existing rows read as unknown and
  show as "your call". `ActionLog` already has `GenreSettled` and `FamiliaritySettled` (`ActionLog.cs:130-131`).
- **Remove from the page:** `Reject` takes its Live removal id; add a page path that records the
  removal without one (the removal then waits for Live to write it, the same way a page-chosen genre
  does).
- **Lead/Sparse** is built as an empty row now (value, override via `Settle`/`Clear`) and shows
  "not measured" until #195 lands; the *measured* origin joins it then. Nothing here waits on #194.
- **Order:** design round trip, then one build agent for genre and familiarity plus the origin
  column, then Lead/Sparse when #195 lands.

## Cost

A Design session (one round, no GPU), then a medium build: about 150 lines of strips and details
markup, 60 for the page remove/add paths, 40 for the origin column and migration, tests per store
(origin written by page and by Live; remove without a Live id; name check), and a Playwright pass
over the song page. Lead/Sparse is a further small step after #195.

## Test plan

- Store tests: a page `Choose`, `Settle` and `Reject` write origin `Page`; `LiveAnswers.Record` writes
  `Live`; an old row reads as unknown.
- A page removal with no removal id is kept and not shown again; a name of his own passes
  `IsName`; one of the 29 replaces his previous pick.
- Song page: each origin string appears for a track in each state; numbers show for familiarity;
  nothing appears for a track with no answers except "not looked up yet".

## Out of scope

Prompts and queues; the Lead/Sparse measurement and cuts (#194, #195); re-measuring familiarity cuts
(#196); changing how genre is learned; writing tags into files.

## For Bamo to confirm

1. **Extend the existing Tags group rather than a new section or page.** Recommended: yes, the
   strips are already there and the Tracks page shares them.
2. **Add an `Origin` column so "set in Live" can be shown.** Recommended: yes; without it Live
   answers read as "your call". Alternative: drop "set in Live" from the list.
3. **Removing a genre from the page is recorded without a Live removal id** and waits for Live.
   Recommended: yes. Alternative: remove from Live only.
4. **Build Lead/Sparse now as an override row that reads "not measured"** until #195. Recommended:
   yes; it is small and the override works without a measurement. Alternative: wait for #195.
5. **Send this to a Claude Design session first**, as `docs/agents/design.md` asks, with this brief
   as its input. Recommended: yes; label #193 `needs-design`. Alternative: a build agent follows
   the existing strips directly.
