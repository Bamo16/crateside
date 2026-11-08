# 0084 - the judge runs and writes in a folder with no tilde

- Status: Accepted
- Date: 2026-11-08

## Context

An imported track's key is `file~` and 16 hex digits. The judge session ran in the track's working
set (`working\<trackKey>\`) and was told to write `verdict.json` there. Claude Code's Write tool
refuses an absolute path with a `~` followed by a digit, because that looks like a Windows 8.3 short
name (`PROGRA~1`); headless, nobody can approve it, so no verdict was written and the stage failed
with "no verdict file was written". A key whose tilde is followed by a letter wrote fine. About 10 of
16 imported tracks were affected. Read is not checked, so the evidence paths in the prompt are unharmed.
Measured from the session logs of `file~86590e76549a8e2c` (2026-11-08).

## Decision

The judge gets a folder of its own, `judging\<key with ~ replaced by ->\` beside `working\`
(`CratesidePaths.JudgingFolder`, `WorkingSet.JudgeFolderFor`). The session's working directory is that
folder and `verdict.json` is written there; `acceptEdits` covers it because it is the working directory.
The track key itself is unchanged, and so is the working set: evidence, windows and the download stay
where they were, and the judge only reads them.

The `-` replacement cannot collide: video IDs are 11 characters and a `file-` name is 21.

`WorkingSet.Discard` removes the judging folder with the working set. Existing `file~` working sets
need nothing: no reader of the working folder changed. A stale `verdict.json` in an old working set is
ignored; the runner deletes the new path before each session.

## Rejected

Renaming the working folders for every key would have touched the janitor, the source index and every
path already on disk, for a problem that only Write has.
