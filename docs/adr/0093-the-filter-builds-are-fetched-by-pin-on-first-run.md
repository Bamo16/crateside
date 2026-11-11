# 0093 - the filter builds are fetched by pin on first run

- Status: Accepted
- Date: 2026-11-11
- Built by [#163](https://github.com/Bamo16/crateside/issues/163).
- Extends [ADR 0066](0066-keydetect-has-its-own-pinned-build.md): `builds.json` stays the override.

## Context

Each filter's build came from `builds.json`, seeded from `Paths:KeydetectFfmpeg` and
`Paths:BeatdetectFfmpeg`, which default to the `ffmpeg` on `PATH`. A user had to find and install a
build carrying the filters. FiLL publishes each filter's builds as releases (a zip and a
`SHA256SUMS.txt`) and agreed to crateside fetching them, provided nobody charges for them; the
repositories are LGPL-2.1.

## Decision

- **One build per filter is pinned in code** (`BuiltInBuilds`): the release tag, the zip's SHA-256 and
  the SHA-256 of the executable inside it, per system (Windows and Linux builds exist; none for
  macOS, which keeps the old seeding). The pins are keydetect v1.3.0 and beatdetect v1.4.0, the
  releases the maintainer's pins in `builds.json` already read on. **Measured 2026-11-11:** the
  executables in those two releases hash to the maintainer's pinned builds, so readings stay
  comparable. keydetect has since reached v1.6.0 and beatdetect has not moved.
- **Fetched on first run** into `%LOCALAPPDATA%\crateside\tools\<filter>-<tag>\`, only when the filter
  knows no pinned build and its path is not configured. The zip is checked against its pin, the
  executable is extracted and checked against its own, and only then moved into place. A mismatch, a
  missing entry or a failed download is logged as an error and installs nothing; the filter then
  seeds from its configured path as before.
- **`builds.json` is the override.** A pinned build whose path is not under `tools` is never replaced,
  and neither is a build named in configuration. A pin that an earlier crateside fetched moves to the
  current one when the code's pin changes; the old build stays known and on disk. The re-read of
  evidence from another build (ADR 0066) covers the readings, and nothing is re-judged.
- **madmom and edma still decode through `Paths:KeydetectFfmpeg`**; a general-purpose ffmpeg for them
  waits for StemForge's (Bamo16/StemForge#100).

## Consequences

- A new pin ships with a crateside update, by editing `BuiltInBuilds` after reading the release's
  `SHA256SUMS.txt`.
- `Paths:BeatdetectFfmpeg` had no machine-specific default in the repository (it is `ffmpeg`);
  any such path was set in the maintainer's own configuration, not removed here.
- The fetch itself, from FiLL's server, was exercised only against a fake downloader; the pinned
  hashes were checked against a manual download of both zips.
