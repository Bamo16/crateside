# Handoff: build crateside v1

**Date:** 2026-08-12
**For:** a fresh agent, in a new VS Code window on `V:\Repos\crateside`
**Prior session:** a `/grill-with-docs` design session in `V:\Repos\keytag`
(commit `4642a8b`). **This repo is docs only — no code yet.**

## Read these first

- [`../CONTEXT.md`](../CONTEXT.md) — this repo's glossary (pairing vocabulary, views)
- [`V:\Repos\keytag\CONTEXT.md`](../../keytag/CONTEXT.md) — shared domain language
- [`V:\Repos\keytag\docs\adr\0003-...`](../../keytag/docs/adr/0003-a-separate-blazor-app-owns-the-human-half.md)
  — **the design, with every rejected alternative and why.** Read it fully before
  proposing changes; most obvious objections were already argued.
- [`V:\Repos\keytag\results\README.md`](../../keytag/results/README.md) — the
  `runs.jsonl` schema this app consumes

## Settled — do not relitigate without cause

| decision | |
|---|---|
| Separate repo, one-way dependency | crateside reads keytag; keytag never knows it exists |
| Blazor Server, `InteractiveServer`, **.NET 10** | .NET 10 SDK + ASP.NET runtime confirmed installed; a .NET 11 preview is also present — do not target it |
| Staging board is **purely derived** | no store, no done-flags, no "shelved" state |
| Row = **song**, stems rolled up | key is song-level; completion is per-stem and ragged |
| Lookup = **video ID → song identity → latest run** | resolving to identity first is what makes supersedes win |
| Partners returns **keys, not files** | Live Lite has no extension API; the user filters in Ableton's browser |
| Ordering: **\|shift\| first, then circle distance** | the existing model; reproduced against real output |
| **All 24 keys always reachable** | max-shift narrows on request, never caps by default |
| Mashup model is **ported**, not shared | duplication accepted knowingly |
| App owns the **Queue**, OAuth and deletions | keytag stays pure Python with no secret |
| Removal predicate = **fully processed** | ledger-driven, idempotent, immune to partial failure |
| Human work is done when stems **leave staging** | location is the signal; `.asd` is not (see below) |
| Processing stays **ad-hoc**; scheduling is v2 | ~2h serialized GPU per batch |

## Verified facts (measured, not assumed)

**The video-ID join works.** StemForge embeds `source: <url>` in each stem's
`COMMENT` tag at separation. It survives renaming, where filenames do not — a file
named `Alan Gomez - Mission 15` has ledger identity
`alan gomez, luck ra - luck ra | mission 15`. Matched **7/7 songs** in staging;
**124/125** ledger runs carry an extractable video ID.

**Read those tags with TagLibSharp, not ffprobe** — in-process managed code, no
external binary, no stdout parsing. `TagLibSharp` 2.3.0 is the package.

**The writer already has a reader: mirror it.**
`V:\Repos\StemForge\src\StemForge.Core\Separation\AudioTagger.cs` both writes the
provenance comment (`BuildProvenance`) and parses it back (`ParseProvenance`, used
when a previously-tagged file is fed back in as a local input). Copy that ~30-line
parse; **do not project-reference `StemForge.Core`** — it targets **net11.0**,
which crateside must not, and it drags in CommunityToolkit.Mvvm, SharpCompress and
the Python separator tooling for what is a string split.

The contract, from `BuildProvenance`:

- One line joined by `" | "`, always led by `stemforge/<version>`.
- `ParseProvenance` gates on that `stemforge/` prefix and returns all-nulls
  otherwise. **Keep the gate** — it is what makes an untagged or foreign file read
  as "no video ID" rather than as garbage.
- Fields are `key: value` with a literal space; the one crateside needs is
  `source: <url>`, from which the video ID is the `v=` query parameter.
- Read it through `f.Tag.Comment`, exactly as StemForge writes it. Going at the
  Xiph field by name is unnecessary — the round-trip is already proven by
  StemForge's own local-file path, and it keeps crateside format-agnostic if a
  stem is ever not FLAC.

**Prefer managed .NET libraries over shelling out to binaries in general** — that
holds beyond this one case; see the Queue below.

**The XMP contract**, inferred from the files and checked across both roots (19 and
430 entries):

- One `Ableton Folder Info\<uuid>.xmp` per **root folder**. The UUID is **not** a
  folder identifier — it is byte-identical in both roots, so it is
  per-user/per-install. **Key on the containing root folder, never this filename.**
- `ablFR:filePath` is **relative to its root**, so equal names under different roots
  do not collide.
- Ableton **migrates an entry when the file moves**. Zero `filePath` overlap between
  roots; zero entries for absent files. Self-cleaning — no reconciliation needed.
- An entry exists for any file with *any* keyword, and keywords are independent.
  **Tagged means "has a `Key|` keyword"**, not "has an entry" (19 entries covered 20
  staged files).
- Keys are two separate keywords using Unicode ♯ (`Key|G♯` + `Key|Major`); the
  ledger spells ASCII (`G# major`). Recombine and normalize on read.
- **Ableton owns this file while running** — read only, never write (keytag ADR
  0001). The board can lag until Ableton flushes.

**A `.asd` sidecar does NOT mean "warped"** — corrected by the user 2026-08-12,
after the reader had been built on the wrong reading. It means only that Ableton
has *analysed* the file, which happens on dragging a clip into Arrangement view.
It is present today on stems where the user clicked warp and then stopped because
they were not ready to do the work. **It is not evidence that anything is
finished, and crateside must not treat it as progress.** The existence check has
been deleted rather than left in as a misleading signal.

**Completion is a file's location, not a flag.** The human work is done when the
stems leave `P:\RYAN\Ableton\Sample Staging` for `P:\RYAN\Ableton\Mashup Samples`.
This is the same self-cleaning property the XMP has: nothing is marked done,
things simply move. It also keeps the staging board purely derived.

*Consequence for the board:* there is **no reliable "warped" signal at all**, so
the board cannot have a warped column. A staged song's per-stem progress reduces
to whether it is key-tagged; leaving staging is the completion event, and a song
that completed is simply no longer on the board.

*The sidecar may still be worth reading one day, for a different reason.* If warp
markers can be extracted, they are **ground-truth tempo** — the user's own warp,
not a detector's guess — which would beat both the online BPM and `beatdetect`.
That is a future capability, not a v1 one. What is in the file (inspected, not
merely assumed): ~400 KB of Ableton's undocumented binary serialization —
`SampleData`, then repeated `RemoteableDouble` / `RemoteableBool` / `UserFloat` /
`RemoteableEnum` typed fields, with the bulk almost certainly waveform overview.
There is no published spec; this would be reverse-engineering, and it should be
its own decision with its own ADR, not a quiet addition.

**The library is fully readable** — 435 files, 429 key-tagged, `Type|` values
Instrumental 221 / Vocal 206 / Loop 2. Not needed for v1 (Partners returns keys),
but it is there if the design is ever revisited.

## The ported model

Source: `P:\RYAN\Streaming\Code\PowerShell\PSMashup` (its own git repo, `net10.0`).
Take `Models\MusicKey.cs`, `Models\MashupPartner.cs`, `Enums\`. **Strip the
`[Hidden]`/`[Alias]` attributes** — they are `System.Management.Automation` and
would drag `Microsoft.PowerShell.SDK` into a web app. Do not modify the PSMashup
repo; the cmdlet keeps working as-is.

The model, confirmed by reproducing the user's real output for D♯ Major:
`score = circleDistance*10 + |shift|`, ordered by `|shift|` then `score`; exact
match scores 0, relative 0.1. Yielded 0, 0.1, 10, 10, 20, 30, 40 — matching the
printed order exactly.

`CONTEXT.md` flags **"Vibe"/`Modulation`** as a lossy catch-all. Improving it is
optional and is a deliberate behaviour change from the cmdlet — not a silent fix.

## The Queue (OAuth)

- **Read is `playlistItems.list` over REST** — use the `Google.Apis.YouTube.v3`
  client, not `yt-dlp --flat-playlist` (ADR 0003 says yt-dlp; that predates this
  correction and the ADR should be amended). No subprocess, typed results, and it
  returns `snippet.resourceId.videoId` directly instead of a scraped URL. **Quota
  cost is 1 unit** against a 10,000/day default, and the call paginates
  (`nextPageToken`, 50 per page) — a queue of any realistic size is a handful of
  units per refresh.
- **The Queue playlist is currently unlisted**, so a **long-lived API key** can read
  it and never expires. But **the user may change its visibility**, and a *private*
  playlist needs OAuth even to list. So: **read with the OAuth credential as the
  primary path** — it works at every visibility — and keep the API key as the
  fallback that keeps the board alive while the write token is lapsed. Do not build
  the read on the API key alone; that path silently dies the day the playlist goes
  private.
- **Guard the visibility-change failure mode.** An API-key read of a playlist that
  has gone private is expected to come back as *not found* rather than as a
  permission error, which a naive reader renders as an empty queue — indistinguishable
  from a genuinely empty one. Surface "could not read the playlist" as its own state,
  distinct from "nothing queued".
- Note that the built-in **Watch Later** playlist is not reachable through the API
  at all; the Queue must be an ordinary playlist.
- **Delete needs OAuth user consent** (`playlistItems.delete`); an API key cannot
  write. Needs a Google Cloud project + OAuth **Desktop/installed app** client.
- **The write token expires every 7 days** for an unapproved app and re-consent is
  interactive. Known from the user's prior experience, and **accepted** — do not
  redesign around it. Read-scoped tokens last far longer.
- **Because reading can fall back to the API key, expiry is survivable:** only
  deletion breaks. Treat an expired token as an ordinary state with a re-consent
  button, not an error. (This holds only while the playlist stays unlisted — if it
  ever goes private, a lapsed token blanks the view too, and the re-consent button
  becomes the only way back.) Being a browser app, the `http://localhost:<port>/…`
  redirect flow is first-class.
- **Never commit the token.** Gitignored below; consider DPAPI at rest.
- Removal rule: delete any playlist item whose video ID is **fully processed** —
  a ledger verdict **and** a stem carrying that video ID on disk. Idempotent; a run
  that recorded a key then died during separation fails the test and stays queued.

## Suggested order

1. **Spike the readers first, headless** — XMP parse, `.asd` scan, `runs.jsonl`
   parse, tag-based video-ID extraction — and assert against the real folders. The
   XMP contract above is the only load-bearing inference in the design; prove it
   before building UI on it.
2. **Staging board.** Delivers the value that motivated the project.
3. **Partners.** Port the model, then the UI. Self-contained, no I/O.
4. **Queue.** Read-only listing + diff first; add OAuth deletion last, since it is
   the only part needing credentials.

## Open

- **What if the user simply deletes stems instead of moving them?** Raised
  2026-08-12, unaddressed. Deletion and completion look identical from staging —
  both are "no longer here" — but they mean opposite things, and the Queue's
  removal predicate requires *a stem carrying that video ID on disk*, so a deleted
  stem silently keeps its track queued forever. Not blocking the staging board;
  decide it before the Queue's deletion path.
- Nothing else blocking. `docs/adr/` here is empty by design — add one when crateside
  makes a decision of its own; the architecture is already recorded in keytag.
- Paths are hardcoded to this machine (`P:\RYAN\Ableton\…`, `V:\Repos\keytag`).
  Config vs constants is a v1 call, unmade.
