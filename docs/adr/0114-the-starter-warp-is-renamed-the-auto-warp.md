# 0114 - The starter warp is renamed the auto warp

- Status: Accepted
- Date: 2026-11-27
- Decided by the maintainer on 2026-11-27. Renames the warp of
  [ADR 0047](0047-a-starter-warp-and-where-transients-came-from.md); every later ADR that says
  "starter warp" means the same thing.

## Context

The Settings page, the Tracks filter and the dashboard already said **Auto warp**. "Starter" dated
from when beatdetect's one-marker warps were only a starting point for the maintainer's own warp,
and it survived in the code, the API and the docs. Two names for one thing made every conversation
translate.

## Decision

- **One name, everywhere current**: **auto warp**. Types and members (`AutoWarpGrid`,
  `WrittenAutoWarp`, `WarpOwner.AutoWarp`, `AutoWarps` for the setting), files and folders, Razor
  components, CSS ids and classes, API routes and JSON (`owner.kind` is `autoWarp`,
  `written.autoWarp`), settings, log messages, tests and their names, and the docs outside the ADRs
  (`docs/auto-warps.md`, formerly `docs/starter-warps.md`).
- **History keeps its words.** Existing ADRs, the dated notes under `docs/notes/`, the frozen design
  briefs, handoffs and canvases, closed issues and commits say "starter warp" and are not edited.
  `CONTEXT.md` names the term **Auto warp** and carries "formerly starter warp", so old text resolves.
- **Old persisted names are still read**, each store keeping the form that fits it:
  - `warp-settings.json` (`starterWarps`), `written-warps.json` and `saved-warps.json`
    (`starter`, `fromStarter`) and `live-analyses.json` (`wasStarter`, `starter`) are read through
    `ReadFileOrDefault` with a table of old property names (`LegacyWarpNames.StoredKeys`): an old
    name loads as the new one, at any depth and in any casing, unless the new name is also there.
    Nothing is migrated at start-up: each store writes its whole file on its next change, which
    writes the new names, and until then the old file reads as the same values.
  - `Warps:StarterWarps` in an appsettings file or environment variable is read when `Warps:AutoWarps`
    is not set. The Settings choice in `warp-settings.json` still wins over both.
  - The `owner.kind` of the warp API changed from `starter` to `autoWarp`. It is output only, never
    stored or read back, so there is nothing to keep loading; a client that matches on the old value
    has to follow.
- **Nothing in the ledger** (`ledger.db`), `jobs.jsonl` or the other stores under `%APPDATA%\crateside`
  names a starter, so they are untouched. Job summary lines already written keep their text
  ("starter warp at 126.00 BPM") and are only text.

## Consequences

- A grep for "starter" now finds the ADRs, the frozen notes and design records, the compatibility
  names in `LegacyWarpNames` and its tests, and this record.
- A rollback to an earlier build reads none of the new names in those four files, so an auto warp
  written by this build would read as no record. Nobody runs an older build against this data.
