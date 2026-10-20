# 0061 — A source download is kept for a set time after its stems reach the library

- Status: Accepted
- Date: 2026-10-20
- Builds [#133](https://github.com/Bamo16/crateside/issues/133).

## Context

The rule that a track's download stays until no stem of it is left in staging works: the downloads
once reported missing were agents searching by song name. What goes wrong is different. The sources
are gone by the time a measurement wants the full mix, so tests run on instrumentals, and at least
twice an agent downloaded half the library's sources for one test and deleted them. A number
measured on an input other than the one a question is about is not wanted.

## Decision

- **A retention setting**, in Settings and kept in `%APPDATA%\crateside\source-retention.json`: a
  number of days, or *Forever*. The default is zero days, which is the old behaviour.
- **The clock starts when the last stem of the track leaves staging.** The janitor records that
  moment in `left-staging.json` in the working set when it first sees the track finished with, and
  does not infer it from file times. A track that left before this shipped has no record, so its
  clock starts at the first sweep. The record is cleared if stems come back to staging.
- **A changed setting applies to everything held**, at the next sweep (every ten minutes).
- **A judging run's download is still discarded as today.** It joins the store only when a record
  of the moment exists, which a backfill writes before it downloads.
- **No second copy where the library keeps the source as a sample.** A track whose plan keeps its
  source (`StemPlan.KeepSource`, an instrumental release) has the library's sample, so its download
  is discarded as soon as it is finished with, whatever the retention. **MEASURED 2026-10-20:** for
  five tracks with both the held download and the kept sample on disk (1991 – *If Only*, Daft Punk –
  *Around the World*, Kendrick Lamar – *HUMBLE.*, Snail's House – *CatJammer*, Telefon Tel Aviv –
  *Fahrenheit Fair Enough*), the decoded MD5s of the two files were identical. The kept sample is
  a copy of the download (ADR 0019), so this is what the code already says.
- **One lookup, by video ID or song:** `%LOCALAPPDATA%\crateside\source-index.json`, a list of
  `{ videoId, song, location, path }` for every source held in the working set (`WorkingSet`) or
  kept as a sample (`KeptSample`). crateside rewrites it whenever the Tracks view reads, when its
  content changed. Working-set folders are still named by video ID.

## Consequences

- With *Forever*, the working set grows by about 45 MB a track (GUESSED from 68 downloads at 3.0 GB).
- A source kept as a sample is listed, and not downloaded again.
