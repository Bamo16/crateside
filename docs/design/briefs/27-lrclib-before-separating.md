# crateside: ask LRCLIB before separating, so an instrumental starts with the Instrumental recipe

Issue: #184 (look a track up on LRCLIB before separating it)

For an implementing agent; Bamo reviews first. Terms (recipe, plan, track key) are in `CONTEXT.md`.
ADRs touched: 0077 (recipes, the near-silent vocal flag), 0018 (the plan is the record), 0083
(instructions and intake live in the ledger database), 0090 (says lyrics are not used for
Lead/Sparse; this brief uses LRCLIB for something else and does not contradict it).

## Problem

A track that is already an instrumental is separated for six or seven minutes of GPU into a vocal
stem of silence, and only afterwards does the loudness stage flag it ("vocals near-silent").
Bamo measured on 2026-11-04 that where LRCLIB knows a track, its `instrumental` mark agreed with
the library every time (26 of 44 instrumentals marked, 0 of 153 vocal tracks marked; the rest
not found). The numbers are in the issue; `docs/notes/2026-11-04-lrclib-instrumental-review.md`,
which the request names, is **not in the repo** (not in `docs/notes/`, and git history has no such
path), so this brief does not cite it.

## Current behavior

- **No LRCLIB code exists.** The only mention is ADR 0090's line that lyrics are not used.
- **A recipe is a plan.** `Recipes.PlanOf` (`src/Crateside.Core/Imports/Recipe.cs:20`) maps
  Standard to `StemPlan.Default` and Instrumental to `StemPlan.InstrumentalRelease`
  (`Plans/StemPlan.cs:20`: no presets, keep the source; `WorkOrder.cs:79` adds the drum guide).
- **A plan of null means "not said"** (`WorkLists/TrackInstruction.cs:15-19`). The default recipe
  is written once, at arrival, and only when it is not Standard: `WorkListService.ApplyDefaultRecipe`
  (`WorkLists/WorkListService.cs:98-112`), `TrackAdd.cs:121`, `TrackIntake.cs:221`. A folder-named
  recipe or an edited plan is a non-null plan too. So **"left alone" is exactly `Plan is not null`**.
- **What holds separation back** is `SeparationSelector.WhyNot` (`Orchestration/Separation/SeparationSelector.cs:65-76`),
  returning a `SkipCause` (`Orchestration/Jobs/Selection.cs:20`). Separation needs the downloaded
  audio first (`SkipCause.NoAudioYet`), so the source file and its duration exist by the time a
  track is eligible.
- **The after-the-fact check** is `NearSilentVocal` (`Loudness/NearSilentVocal.cs`), a Tracks-row
  flag (`Pages/Tracks.razor:458`) whose button re-cuts as Instrumental (`Components/StemEditor.cs:241`).
  It stays as it is.
- **A lookup pattern to copy:** `GenreLookup` (`Genres/GenreLookup.cs`: a named client, `GetAsync` at
  :154, returns null when the source could not be reached, which is worth asking again, and an
  empty answer otherwise) driven by `GenreWorker` (`src/Crateside/GenreWorker.cs`: `NewWork`, one
  at a time, never paused). The shared HTTP defaults set the User-Agent
  `crateside/1.0 ( https://github.com/Bamo16/crateside )` and a 15 s timeout
  (`ServiceRegistration.cs:183-190`), which is what LRCLIB asks for.
- **The song's name** is `TrackRow.ArtistAndTitle` (`Tracks/TrackRow.cs:174`): null while a row has
  only the upload's title; set once `NameWorker` has resolved it, or from an import's tags.

## What LRCLIB returns (read-only, 2026-11-17, generic User-Agent, five requests)

- `GET /api/get?artist_name=Daft Punk&track_name=Around the World&duration=429` found the entry
  (its duration 430): a JSON object with `instrumental: false`, `plainLyrics`, `duration`. A miss is
  a plain **404**. `duration` matched within a second.
- `GET /api/search?q=Daft Punk Around the World` returned 20 entries, several with a **title that
  carries the artist** ("Daft Punk - Around the World", "...(Official Audio)") and one at 242 s
  against the others' 430 s. So search results are noisy; only a duration-matched entry is usable.
- `GET /api/search?track_name=Strobe&artist_name=deadmau5` returned entries with `instrumental: true`
  (all five first hits agreed, durations 632 to 634 s; one title read `Strobe;Strobe`).

## Design

### 1. The lookup

`Lrclib/LrclibLookup` (a new domain folder in `Crateside.Core`, beside `Genres/`), a named client,
one answer type:

- `Instrumental`, `HasLyrics`, `NotFound`, or `NoAnswer` (the source could not be reached).
- **Order of asking** is the issue's: `/api/get` with the full artist, title and the **source's
  duration** first; when that is a 404, `/api/search` by the first credited artist alone and the
  title with a version tail removed (`(Extended Version)`, `(Radio Edit)`, `(Original Mix)`; a
  remix's bracket stays). The issue's "duration before the download" branch is dropped: the lookup
  runs after the download, so the duration is always the file's (TagLib, as `BeatReader.cs:445`
  reads it).
- **A search hit counts only if** its duration is within 3 seconds of the source's and its artist
  name matches the asked one without case. The answer is `Instrumental` only if **every** counted
  hit says so; one dissenting counted hit makes it `HasLyrics`. A `/get` hit alone decides.
- Lyrics are read for the flag and then dropped; nothing of them is kept.
- One request every 500 ms at most. LRCLIB states no limit; this is courtesy.

### 2. Keeping the answer

A table `LrclibAnswers` in `ledger.db` (track key, outcome, asked-at, attempts, and whether it
changed the plan), one EF migration after `20261110212347_LiveAnswers`. It is not a column on the
intake record: `IntakeStore.TakeIn` is called only from `TrackIntake` (`Playlist/IntakeStore.cs:61`),
so it cannot be assumed for a playlist track. A `NoAnswer` is retried on the worker's next tick
until three attempts, then settles; it never holds a track longer than that.

### 3. Holding separation, then choosing

- `LrclibWorker`, shaped like `GenreWorker`, takes rows with a downloaded source, a non-null
  `ArtistAndTitle`, no recorded answer and `Plan is null`. On `Instrumental` it calls
  `instructions.SetPlan(key, Recipes.PlanOf(Recipe.Instrumental))` and records "changed the plan".
  Any other answer changes nothing.
- `SkipCause.AwaitingLookup` is added at `Selection.cs:20` and returned from `WhyNot` for a track that
  has a name, a null plan, a downloaded source and fewer than three attempts with no answer yet. A
  track with no resolvable name is never held. The Tracks page's GPU line already shows a skip's
  cause, so the row says "asking LRCLIB" while it waits.

### 4. The row says why, with one click

Beside the near-silent flag (`Tracks.razor:458`), a flag `LRCLIB lists it as instrumental` shows
while the track's plan is still the one the lookup wrote. Its button, *Separate in full*, sets the
plan to an explicit `StemPlan.Default` (not null, so the answer is not applied twice); after
separation it is the same re-cut the near-silent flag already runs (`StemEditor.cs:241`). The flag
goes once the plan is anything else.

## Cost

One agent, one session, no GPU, no quota. About 120 lines for the lookup, 100 for the worker, 80
for the store and migration, 15 in the selector, 30 in the Tracks row and `TrackRow`, and tests.
A new track costs one to two requests to LRCLIB. ADR 0077 gets an addendum (a new ADR, next free
number, only if the hold in point 1 below is accepted).

## Test plan

- `LrclibLookupTests` with `FixedHttpClientFactory` (no network): a `/get` instrumental hit; a 404
  then a search whose counted hits all agree; a search with one dissenting counted hit; a search
  hit 20 s off the duration (ignored); a version tail stripped and a remix bracket kept;
  `NoAnswer` on a failed request.
- `SeparationSelectorTests`: a named track with null plan and no answer is `AwaitingLookup`; with an
  answer, with a non-null plan, with no name, or after three failures it is not.
- Recipe choice: `Instrumental` writes the Instrumental plan on a null plan; the same answer on a
  folder-named or edited plan changes nothing; `HasLyrics` and `NotFound` change nothing.
- On the library, with the app run by Bamo: Deadmau5 *Strobe* (LRCLIB marks it instrumental)
  arriving new starts as Instrumental with the flag; a vocal track (Daft Punk *Around the World*)
  starts as Standard; a video game track (not on LRCLIB) starts as Standard and is not held.

## Out of scope

Keeping lyrics; lyric-based Lead/Sparse (ADR 0090); tracks already separated; the near-silent
flag's threshold; any LRCLIB write.

## For Bamo to confirm

1. **Hold separation until LRCLIB has answered (bounded by three tries).** Recommended: without the
   hold the GPU often starts before the answer, which defeats the point. Alternative: ask in the
   background and never wait; an instrumental that was already cut is then caught by the
   near-silent flag as today.
2. **Accept `Instrumental` only when every duration-matched hit agrees**, a `/get` hit deciding
   alone. Recommended: one wrong mark was seen in 2026-11-04 (*Caramelldansen (Speedy Mixes)*), so a
   disagreement should cost nothing. Alternative: the first hit decides.
3. **Look up tracks already in the queue, not only new arrivals.** Recommended: yes, any track with
   a null plan that has not been separated; it costs a request each. Alternative: new arrivals only.
