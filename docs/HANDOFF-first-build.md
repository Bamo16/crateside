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
| Processing stays **ad-hoc**; scheduling is v2 | ~2h serialized GPU per batch |

## Verified facts (measured, not assumed)

**The video-ID join works.** StemForge embeds `source: <url>` in each stem's
`COMMENT` tag at separation. It survives renaming, where filenames do not — a file
named `Alan Gomez - Mission 15` has ledger identity
`alan gomez, luck ra - luck ra | mission 15`. Matched **7/7 songs** in staging;
**124/125** ledger runs carry an extractable video ID.

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

**Warped** = a `.asd` sidecar exists next to the stem. Independent of tagged: real
data had a song tagged-but-unwarped and another both.

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

- **Read needs no auth** — `yt-dlp --flat-playlist`.
- **Delete needs OAuth user consent** (`playlistItems.delete`); an API key cannot
  write. Needs a Google Cloud project + OAuth **Desktop/installed app** client.
- **The write token expires every 7 days** for an unapproved app and re-consent is
  interactive. Known from the user's prior experience, and **accepted** — do not
  redesign around it. Read-scoped tokens last far longer.
- **Because reading is unauthenticated, expiry is survivable:** only deletion
  breaks. Treat an expired token as an ordinary state with a re-consent button, not
  an error. Being a browser app, the `http://localhost:<port>/…` redirect flow is
  first-class.
- **Never commit the token.** Gitignored below; consider DPAPI at rest.
- Removal rule: delete any playlist item whose video ID is **fully processed** —
  a ledger verdict **and** a stem carrying that video ID on disk. Idempotent; a run
  that recorded a key then died during separation fails the test and stays queued.

## Suggested order

1. **Spike the readers first, headless** — XMP parse, `.asd` scan, `runs.jsonl`
   parse, ffprobe video-ID extraction — and assert against the real folders. The
   XMP contract above is the only load-bearing inference in the design; prove it
   before building UI on it.
2. **Staging board.** Delivers the value that motivated the project.
3. **Partners.** Port the model, then the UI. Self-contained, no I/O.
4. **Queue.** Read-only listing + diff first; add OAuth deletion last, since it is
   the only part needing credentials.

## Open

- Nothing blocking. `docs/adr/` here is empty by design — add one when crateside
  makes a decision of its own; the architecture is already recorded in keytag.
- Paths are hardcoded to this machine (`P:\RYAN\Ableton\…`, `V:\Repos\keytag`).
  Config vs constants is a v1 call, unmade.
