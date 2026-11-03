# 0074 — A data root moves all of crateside's state, and a demo library fills it

- Status: Accepted
- Date: 2026-11-03

## Context

The README's screenshots could not be retaken after a redesign: the app reads the maintainer's own
library, Live's file index, Claude Code's login and `%APPDATA%\crateside`, so a shot shows whatever
that machine holds that day, real song names included.

## Decision

- **`Paths:DataRoot`** (environment: `Crateside__Paths__DataRoot`) rebases every path crateside
  derived from `ApplicationData` or `LocalApplicationData` to `<root>\state\`, the Live file index
  and the Claude Code credentials file included, and gives the Staging, Library and Ableton projects
  folders and the stem destinations a default under `<root>`. Unset, nothing changes. A path set by
  name still wins.
- **It is `CratesidePaths` alone.** Each store that took an optional path now takes
  `CratesidePaths` and reads its file from it; a test that wants a bare path uses the store's
  `internal` constructor. No store looks in the profile on its own.
- **`Workers:Disabled`** (environment: `Crateside__Workers__Disabled`) drops every hosted service,
  as the document build does. Nothing downloads, separates, judges, warps or looks a song up, and
  the quota reads from a credentials file under the root, which is empty, so no request is made.
  **`Queue:Offline`** (environment: `Crateside__Queue__Offline`) stops the Tracks page reporting
  that the playlist could not be read, which an instance with no playlist would otherwise say.
  A data root also reads no user-secrets or `appsettings.Secrets.json`.
- **`demo/Crateside.Demo`** writes a demo library into a folder: seventeen well-known songs spread
  over every stage, state written through crateside's own stores so it cannot drift from the
  formats, and no audio beyond a one-second file of silence for each stem a warp sidecar sits beside
  (TagLib reads its sample rate; nothing decodes it). Every other stem is an empty file, its tags
  seeded in the stem tag cache. Jobs for separation and judging are written as running, which with
  workers off stay running.
- **The verdicts are real.** `demo/Crateside.Demo/verdicts.json` holds what crateside recorded for
  thirteen of those songs (key, confidence, sections, sources, candidates, the detectors' report),
  kept so the shots show honest evidence. Upload IDs, paths and anything of the maintainer's own
  are not in it.
- **`scripts/screenshots.py`** builds that library, starts crateside on port 5099 against it and
  takes the README's screenshots, so all of them come from one state. Relative times ("3 min ago")
  and the dates of the history follow the day the library is written, since the pages read the
  clock directly.
