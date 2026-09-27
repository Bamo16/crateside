# 0040 — a rename relinks the sets that use the file

- Status: Accepted
- Date: 2026-09-27
- Supersedes [ADR 0039](0039-crateside-renames-in-the-library-and-warns-about-sets.md)'s "the sets
  are not changed". The rest of 0039 stands.

## Context

ADR 0039 let crateside rename files in `Mashup Samples` and warned, before the rename, about the
sets that would then show a file as missing. The user had misread the options put to them and
wanted the rename to update those sets, which was the recommended option.

A set is a gzipped XML file. It refers to a sample by a `<FileRef>` holding a relative path and a
full path, and Ableton writes the same reference again inside `<OriginalFileRef>` as
`<FileRef Id="…">`. Ableton escapes only `&` in these paths (measured across the user's 33 sets).

Ableton does not lock a set it has open; it holds it in memory and writes it back on the next save.
A set rewritten underneath it would be overwritten with the old paths, so crateside has to know
which set is open. Two signals were measured on 2026-09-27:

- **Ableton's `Log.txt`** logs each set it loads, but not *Save As*, and it went ten hours without a
  line while Ableton sat on a start-up dialog.
- **Ableton's window title** names the open set (`Internet Gucci (S3RL x Kreayshawn) - Ableton Live
  12 Lite`, with `*` while unsaved), and follows *Save As*. It names nothing while a dialog or the
  splash screen is up.

## Decision

- **A rename rewrites every set that uses a renamed file**: the name in the relative path and the
  whole full path, and nothing else in the file.
- **Each set is copied first** to its project's `Backup` folder as
  `<set> [crateside yyyy-MM-dd HHmmss].als`, beside Ableton's own backups. The new set is written
  beside the old one and swapped in.
- **The open set is read off Ableton's window title.** A rename refuses when the open set uses one of
  the files, and when Ableton is running but its title names no set. A file no set uses is renamed
  whatever Ableton shows.
- The sets to be updated are listed in the rename editor before it is confirmed, as ADR 0039 had the
  warning.

## Consequences

- **crateside edits the user's projects.** Checked by relinking copies of all 33 real sets: only the
  lines naming the sample changed, `OriginalFileRef` included, and afterwards no set referred to the
  old path.
- **Not yet checked in Ableton:** that a relinked set opens with the sample found.
- A set that cannot be rewritten is named after the rename, to be relinked by hand; its backup
  holds it as it was.
- Two sets with the same name in different projects both count as open when one is.
