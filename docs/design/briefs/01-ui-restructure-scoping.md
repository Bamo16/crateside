# crateside — UI restructure scoping

**For:** a design pass (Claude Design) on crateside's interface.
**Not:** an implementation plan. The pipeline underneath is finished and working; this is
about what the user looks at.

Repository: crateside (this repo). Runs locally at `http://localhost:5027`.

---

## What crateside is

A single-user local web app that turns a YouTube playlist into key-tagged audio stems in the
user's Ableton library. It runs a five-stage pipeline unattended:

**evidence → verdict → separation → handoff → tagging**

The domain vocabulary is defined in `CONTEXT.md` (crateside) and keytag's `CONTEXT.md`
(shared terms: *track*, *stem*, *sample*, *warp guide*, *Sample Staging*, *Mashup Samples*,
*fully processed*). **Read those before designing — the words are load-bearing and the user
cares about them.** Decisions and their measurements live in `docs/adr/`.

Do not re-derive any of that here; it is written down.

---

## The user, and what they actually do

One person. Their own music library. They keep Ableton Live open and work in it for long
stretches.

Their loop:

1. Add tracks to a YouTube playlist whenever they think of one — "I want this eventually",
   never "process this next".
2. crateside does everything automatic: downloads, detects key, judges it with an agent,
   separates stems on the GPU (~6 min/track), moves the stems into `Sample Staging`, and
   writes Ableton key tags.
3. **The human-limited steps**: the user drags stems from `Sample Staging` into a throwaway
   Ableton project, *warps* them (sets tempo, places markers), sometimes renames, then moves
   them to `Mashup Samples` — the finished library.

**The single most important design fact:** now that crateside does the tagging, *the only
thing the user needs from the screen during a warping session is the BPM*, so they know what
tempo to start at. Everything else is monitoring.

A track can sit in staging for weeks. This is not a queue and implies no urgency.

Scale: ~30–40 tracks in flight at a time; 433 finished samples in the library.

---

## What exists today, and why it does not work

Two pages, plus a small third:

- **Queue** (`src/Crateside/Components/Pages/Queue.razor`, 486 lines) — the playlist as work,
  split into sections: *next batch*, *held*, *awaiting stems*, *awaiting a key*, *done*, with
  checkboxes for batch selection and two pause controls.
- **Staging board** (`Board.razor`, 248 lines) — a table of what is in `Sample Staging`: song,
  key, confidence, BPM, per-stem tag state.
- **Song** (`Song.razor`, 43 lines) — a per-track detail page at `/song/{VideoId}`.
- **Partners** (`Partners.razor`) — unrelated: "what keys pair with this one". Keep separate.

### The user's friction list, verbatim in substance

1. Cannot change or observe instrumental status of **held** tracks — and held tracks are
   exactly the ones still waiting for separation, which is the last moment that fact matters.
2. Checkboxes and batching were always confusing and no longer earn their place.
3. **Multiple lists are confusing.** Managing one track means watching it jump between
   sections as its state changes.
4. **Queue and staging board should be one place.** The original reason they were separate —
   the human had to read keys off the board to tag by hand — went away when crateside took
   over tagging.
5. The "instrumental?" toggle is too blunt; wants a preset picker (deferred, see below).
6. **No insight into what is running.** Currently you infer it by scrolling and counting.
7. No notification when a track finishes everything and is ready to work on.

### The root cause

**The UI is organized around crateside's internal scheduler, not around the user's tracks.**
Each stage needs a work list to pull from — that is a real internal need — but those work
lists got rendered directly as page sections. So a track's *address on screen* changes every
time its *state* changes.

---

## The proposed shape

One page. A frozen header, then one filterable list.

### Frozen header (always visible)

Modeled on an Android notification shade — glanceable, never scrolled away. Nav tabs pinned
alongside it.

- **Separation**: what it is working on now, and how many are waiting.
- **Judging**: what it is working on now, how many are waiting, and a pause button.
- **Quota meter**: the user's 5-hour Claude window as a percentage, with reset time. Judging
  costs ~5.4% of a window per track — measured, see `docs/adr/0006-*.md`. A window is roughly
  15–18 tracks.

### One row per track

**Never per stem.** Everything about a track rolls up into its row. The row never moves;
state is a column, not an address.

Collapsed row shows:

| element | source | notes |
|---|---|---|
| Song name | ledger identity, falls back to filename | |
| Key | verdict | only when confident enough to tag with |
| Confidence | verdict | HIGH / MEDIUM / LOW / WITHHELD |
| **BPM** | verdict | *the one number needed mid-warp — keep it prominent* |
| State chip | see below | where it has got to |
| Next step | derived | what will happen, or what is waiting on the user |

Expanding a row shows its stems: label (`Vocal - Full`, `Instrumental - Full`, `Drums`),
whether each is key-tagged, whether it is warped, and any audit findings. A **warp guide**
(drums) is not a sample — it exists to place warp markers against and is deleted afterwards —
so it should read differently from the stems that are kept.

### Filters replace sections

- **In staging** — the warping view, the one they live in.
- **In flight** — something automatic is happening.
- **Needs attention** — failures, tag disagreements, tracks parked for want of a key.
- **Held** — deliberately left alone.
- **Done** — finished and in `Mashup Samples`.

**The audit stops being a page.** Its findings become row-level flags plus the *needs
attention* filter.

### Toasts

Transient corner notification when a track completes every automatic stage — i.e. its stems
are staged and tagged and it is ready to warp.

---

## The states a track moves through

One ordered progression:

```
queued → detecting → detected → judging → judged →
separating → separated → staging → staged → tagging → tagged → (warping) → done
```

Plus three off-ramps that are not failures:

- **Held** — the user set it aside, usually the wrong upload of the right song. Nothing works
  it and nothing removes it.
- **Parked for want of a key** — separated, but the verdict was LOW or WITHHELD so its stems
  cannot be key-tagged. They stay out of staging. *Nothing automatic will move these on;* the
  way out is a better verdict, which is a human decision.
- **Given up** — a stage failed three times.

And one that is genuinely the user's turn: **staged and tagged, awaiting warping.** This is
where a track sits for weeks, and it is what the *in staging* filter is for.

---

## Technical constraints the design must respect

**Blazor Server, `AddInteractiveServerRenderMode()`.** SignalR is live; there is a
`ReconnectModal` for drops. Live updates and toasts are entirely feasible.

**The live-update pattern already exists** — `Queue.razor` subscribes to `DrainControl.Changed`
and calls `InvokeAsync(StateHasChanged)`, so the activity line already updates without a
refresh. What is missing is that *the stages themselves publish nothing*, so list data only
reloads after a user action. That is why the app currently feels like it needs a refresh. The
fix is one notification source, not a framework change.

**A row update is cheap; a full list refresh is not.** `QueueService.ReadAsync()` calls the
YouTube playlist API *and* scans both stem roots on disk. It must not run per event. The
design should assume a cached view with debounced refresh, and rows that update individually.

**Long-running work.** Separation is ~6 minutes per track on the GPU and strictly one at a
time. Judging is ~1 minute and costs metered quota. Both run unattended in the background, so
the header may be showing activity the user did not start.

**Environment.** Desktop browser, often a second monitor beside Ableton. Single user, local,
no auth. Must be glanceable at a distance.

---

## Out of scope, but coming

Design should not solve these, but should not make them awkward either:

- **Separation progress detail.** The separator (`stemforge-cli`, first-party, same author)
  emits nothing until it finishes. Machine-readable progress is filed as
  https://github.com/Bamo16/StemForge/issues/84. Until it lands the honest display is
  "separating X — 4:12 elapsed" plus a queue depth. **Leave room for a progress bar that does
  not exist yet.**
- **A live view of the judging agent.** Buildable now — judging currently runs
  `--output-format json` (one payload at the end) and Claude Code also offers `stream-json`,
  which emits NDJSON per message and tool call. Something like
  https://www.beautifului.dev/ is wanted for this. Not in this pass.
- **Stem requests.** Replacing the instrumental toggle with a preset picker, *including asking
  for extra stems for a track already staged or finished*. Not needed yet. The
  seam exists in `SeparationRecipe.For`.

---

## Reference material

Read these rather than asking for a summary:

- `CONTEXT.md` — crateside's vocabulary; keytag's `CONTEXT.md` — shared terms.
- `CLAUDE.md` — working agreements. Note especially: **measure before recording**, and do not
  run the app with `dotnet watch` while editing.
- `docs/adr/0003-*.md` — Ableton tag store, and what was measured about it.
- `docs/adr/0005-*.md` — the ordered stages and the handoff stage.
- `docs/adr/0006-*.md` — what a verdict costs, in tokens and in quota.
- `src/Crateside.Core/Staging/StagingBoard.cs` — `StagingSong` / `StagingStem` are the fields
  a row can show today.
- `src/Crateside.Core/Queue/QueueItem.cs` — `QueueView` and the current work lists.
- `src/Crateside.Core/Audit/TagAudit.cs` — the findings that become *needs attention*.

## Suggested skills

- **`design`** — to produce the canvas. This is the main one; the deliverable is a multi-
  artboard design of the unified page: default state, *in staging* filter, an expanded row, the
  header mid-separation, and a toast.
- **`find-docs`** — for current Blazor Server guidance if component structure comes up.
- **`grill-with-docs`** — if the shape above needs stress-testing against the domain language
  before it is drawn; the vocabulary in `CONTEXT.md` is precise and worth not breaking.

Once a design is settled, implementation should follow `CLAUDE.md`: commit straight to
`master`, `dotnet csharpier format .` before every commit, and `dotnet test --solution
Crateside.slnx` (365 tests, currently all passing).
