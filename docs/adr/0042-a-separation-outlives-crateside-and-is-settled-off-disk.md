# 0042 — a separation outlives crateside and is settled off disk

- Status: Accepted
- Date: 2026-09-28
- Builds the adopted-run release [ADR 0034](0034-a-running-separation-holds-its-handoff-while-its-process-lives.md)
  left open. Closes [#49](https://github.com/Bamo16/crateside/issues/49).

## Context

Adoption was built so a `stemforge-cli` that outlived a restart would be watched again rather than
killed. It never fired: 5 of 5 interruptions were abandoned, 0 adopted. Measured on 2026-09-27
(comment on #49), both ways out killed the separation:

- **`dotnet run`** holds the app in a Windows job. Measured again on 2026-09-28: its limit flags are
  `0x2000`, *kill on job close*, with neither breakaway flag, so no child can leave the job, and
  every process in it dies when `dotnet` exits. The built exe launched directly runs in no job.
- **crateside's own graceful shutdown** killed the process tree and recorded `cancelled`, by design
  ("quitting cleanly discards the work here").

And one piece was missing: nothing released an adopted run. Its job stayed `Running` until the next
restart marked it abandoned, however well it had finished. Its result is printed on stdout, which
dies with the crateside that started it.

The user chose to fix adoption rather than delete it (2026-09-28).

## Decision

1. **The `run` task starts the built exe**, from the project folder with the launch profile's
   environment, which is what `dotnet run` runs, minus the job. At startup crateside warns in the
   log if it finds itself in a kill-on-close job.
2. **Shutdown leaves a separation running.** The runner no longer kills it or records anything; its
   job stays open for the next start.
3. **A separation's result is read off disk once nobody is left to read its output.** When a run is
   launched, its job records what it promised: how many files, and which destination. Whenever its
   process is found gone with the job still open (on startup, and at the start of every drain
   tick), crateside counts the stems in that destination tagged with the track's video ID and
   written since the process started. Enough of them: `Succeeded`, and they land as a live run's
   would. Fewer: `Failed`, saying how many of how many. A job with no promise (written before this)
   is abandoned as before.

## Consequences

- A separation survives a restart, and one that finishes while crateside is down counts as a
  success on the next start rather than a strike.
- An adopted run is noticed ending within one drain tick: up to two minutes while it holds the GPU.
- The disk count cannot tell a complete file from a truncated one. The handoff already refuses to
  move a track while its process lives (ADR 0034), so this only matters for a process that dies
  mid-write, which the count reads as a shortfall unless every file happened to be there.
- **Stop crateside with Ctrl+C.** Whether VS Code's *Terminate Task* kills the whole process tree,
  stemforge-cli included, is untested.
- Running the app under `dotnet run` still works, but a restart ends the separation, and the
  startup warning says so.

## Amended 2026-09-29: the handoff waits for the settle

The first adopted run seen live (*Radiohead – Creep*) finished, and within the same second the
handoff moved its stems to staging, before the next drain tick counted them. The count found 0 of
3 in the destination and recorded `Failed`, although the stems had arrived.

The handoff waited only while the process was alive (ADR 0034), because a `Running` record whose
process had gone used to stay open for ever. Since this ADR, the settle closes such a record within
one drain tick, paused or not. So **the handoff now waits until the separation is settled**, not
only until its process is gone: an open separation, live or not, holds its stems where they are.

