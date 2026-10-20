# 0060 — The file workers wake on a change, and the timer is the net

- Status: Accepted
- Date: 2026-10-20
- Builds [#132](https://github.com/Bamo16/crateside/issues/132).

## Context

The warp worker (lending, colours, Collections), the levelling pass and the warp-guide keeper each
looked once a minute, so a sidecar saved in Live or a track moved into the library waited up to a
minute to be acted on. A `FileSystemWatcher` had seemed unreliable before: it drops events on a
burst (and raises `Error` instead), loses them while crateside is down, delivers one save as
several events while the file is still being written (Live writes a sidecar through a temporary
file and a rename), and says that a path changed, not what to do about it.

## Decision

**A watcher is a wake-up, never the source of truth.** `FileWatchers` watches Staging's top level
and the library recursively, for audio, `.asd` and folders, and tells `FileChanges`. Each worker
keeps its scan exactly as it was and ends its wait early.

- **Debounced:** a worker wakes 2 s after the last event of a burst, so a save or a move is one
  scan, after the writes finish.
- **A watcher `Error` (overflow) wakes everyone**; the scan covers what was missed.
- **A listener remembers the last wake it acted on**, so a change that lands while its worker is
  scanning is still acted on, not waited out.
- **The timer stays as the net, at 10 minutes** where it was 1: it catches what a watcher missed,
  including events lost while crateside was not running.
- **crateside's own writes wake the workers too**: one extra scan that finds nothing, since every
  scan already skips what is settled. Not suppressed.
- **A change that is not a file also wakes them**: setting a track mark (#128, #86) notifies
  `FileChanges`, so its Collection is written within seconds, not at the next timer.
- **The backing worker is not woken**: it keeps its own five minutes, and the library's Collections
  reach Live at its next pass.

## Consequences

- **Not woken, so now up to ten minutes:** a new verdict's *Key caveat* in staging (it arrives with
  the next file change or the timer).
- **Not measured:** how long Live takes to save a sidecar after the user acts in it, and whether
  the library's drive ever drops events at the rate the levelling pass writes sidecars. The timer
  covers both.
- Both roots are local NTFS drives. A watcher on a network share is less reliable, and the timer
  covers that case.
