# 0039 — crateside renames in the library, and warns about the sets that use a file

- Status: Accepted
- Date: 2026-09-27
- Narrows [ADR 0003](0003-crateside-may-write-abletons-tag-store.md)'s "`Sample Staging` only.
  Never `Mashup Samples`": a rename may write the library's tag store. Tagging stays staging-only.

## Context

A row with any file in `Mashup Samples` could not be renamed; the user was told to rename it in
Ableton. The user decided on 2026-09-27 that crateside has outgrown that boundary.

A rename in the library touches three things:

1. **The audio file and its `.asd` sidecar**, renamed together as in staging, so the warp survives.
2. **The library's tag store**, which Ableton keys by file name, so the tags follow only if the
   entry is renamed too. The staging rename already does this for staging's store.
3. **The user's Ableton sets**, which refer to a sample by full and relative path. Measured on
   2026-09-27: 76 of the library's 870 files are used by 30 of the user's 33 sets, and 3 of those 76
   no longer exist under the name the set expects.

Nothing of crateside's or keytag's own depends on the name: a track is its video ID (ADR 0014).

## Decision

- **A track's files in `Mashup Samples` are renamed with its staged ones**, found by the upload
  their tags name, with their sidecars and their tag-store entries.
- **The sets are not changed.** The user chose this over updating the paths in their sets, and over
  refusing to rename a file a set uses.
- **The sets that use the files are named before the rename is confirmed** (the user's
  requirement). The editor looks them up when it opens, and its confirm waits for the answer. A
  revert on a track with library files goes through the same editor for the same reason.
- Sets are the `.als` files under the projects folder, Ableton's own backups excluded, read for the
  paths they use.

## Consequences

- **A set using a renamed file shows it as missing** until the user relinks it in Ableton.
- **Concurrent writes to the library's tag store are possible, and unlikely.** ADR 0003 could
  leave them untested because the user avoids tagging in staging, keeping any hand tag for the
  library. Since crateside tags, the user rarely hand-tags anything (owned, 2026-09-27). The
  exposure is a rename landing while Ableton writes the same store. **Untested:** the first
  library rename is to be checked in Ableton for its tags.
- Looking up the sets reads every set on each rename, about a second for 33 sets.
