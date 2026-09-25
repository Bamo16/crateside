# 0034 — a running separation holds its handoff while its process lives

- Status: Accepted
- Date: 2026-09-25
- Amends [ADR 0025](0025-a-failed-separations-partial-output-is-not-a-handoff.md). Part of [#49](https://github.com/Bamo16/crateside/issues/49).

## Context

ADR 0025 made the handoff skip a track whose separation is `Failed`. It chose not to skip one that
is `Running`, because an adopted run (one that outlives a restart) is never released. Blocking on
`Running` would then maroon its stems.

On 2026-09-25 the gap bit. *T & Sugah – TumDaDaDum* was separating. Its vocal (22:48:49 UTC) and
instrumental (22:50:02) were finished in the cut root, and the handoff moved them into Sample
Staging at 22:50:21 while the process was still writing. The user then restarted the app. The
process was killed, which is the abandoned path again: 5 interruptions recorded, 0 adoptions. The
drums warp guide was never made. A retry cannot make it either: with the presets on disk, the
recipe has nothing to cut, and a guide is only made alongside new presets.

## Decision

**The handoff also waits while the track's separation process is alive**
(`SeparationInterlock.IsSeparating`: the job is open, and the recorded PID is running with the
recorded start time, the check that already guards the GPU).

This removes ADR 0025's objection. The block depends on a living process, not on the status, so
a `Running` record whose process is gone blocks nothing. An adopted run that never gets released
cannot maroon anything once its process ends.

## Consequences

- A separation's files hand off together, after the process exits, and not as each one lands.
- The adopted-run release in #49 is still unbuilt. It is no longer what keeps this guard unsafe.
