# 0013 — the index is the tag state, and the XMP may never catch up

- Status: Accepted. `AbletonIndexReader` is built and tested; nothing reads it in anger yet.
- Date: 2026-09-09
- Answers issue #12, and un-defers the library-wide audit
  [ADR 0011](0011-the-audit-may-only-claim-what-crateside-wrote.md) parked.
- Settles the hypothesis [ADR 0003](0003-crateside-may-write-abletons-tag-store.md) left open
  about crateside's atomic write detaching the store Ableton reads.

## Context

The user's tags are reliably present in Ableton's browser while crateside reports some of them
missing. ADR 0003 established on 2026-09-02 that the per-root `Ableton Folder Info\<uuid>.xmp` is
a lagging, partial mirror and that Ableton's own index is the authoritative state — then recorded
that the index's location "was not chased down". ADR 0011 deferred library-wide auditing on
exactly that gap.

The index turned out to be a SQLite database, which makes it readable.

## What was measured

All of it read-only, with Live running throughout, on 2026-09-09. Nothing was written to any
Ableton file.

1. **It opens read-only while Live is running.** `Mode=ReadOnly` through
   `Microsoft.Data.Sqlite`: no lock contention, no error, WAL and all. This was the riskiest
   unknown and it is not a problem.

2. **Location, and how to find it again.**
   `%LOCALAPPDATA%\Ableton\Live Database\Live-files-12300.db`, ~21 MB, with `-shm` and `-wal`
   beside it. The number is a **schema version, not an app version**, and that is now read from
   inside the database rather than inferred: the `version` table holds a single row,
   `version=12300, platform=1`. It agrees with the filename, and one such file has served seven
   Live preference directories on this machine (12.3.7, 12.3.8, 12.4, 12.4.1, 12.4.2, 12.4.3,
   12.4.5).

3. **Nineteen tables, and three carry the answer.** Not 22 — that number came from the
   reconnaissance note on issue #12 and does not survive counting.

   ```
   files             10,340   file_id, parent_id, name, place_id, mod_date, file_type, …
   metadata          43,472   file_id, key, value_id  →  metadata_values.id
   metadata_values      756   id, value
   places                 7   file_id, folder_kind, level, name
   ```

   `places` is the anchor and `files.parent_id` walks all the way up to `P:\`, so a place is
   found by the **absolute path it stands for** rather than by `places.name`, which is only a
   display name.

4. **The integer keys are four-character codes.** `metadata.key = 1431004537` is `'UKey'` — a
   user-applied keyword — beside `'Keyw'` for the ones Ableton derives itself. `files.file_type`
   reads the same way: `'flac'`, `'fldr'`, `'xmp-'`. Nothing about this needs a magic number.

5. **The values are the same strings the XMP holds** — `Key|B`, `Key|Major`,
   `Type|Instrumental`, with tonics in U+266F exactly as `AbletonFolderInfoWriter.Tonics`
   spells them. One interpretation now serves both readers.

6. **Coverage, counted through crateside's own two readers rather than by SQL:**

   ```
                             Mashup Samples    Sample Staging
   flac files on disk                   579                24
   keyed via the index                  576                24
   keyed via the XMP                    482                24
   keyed via either                     576                24
   index only                            94                 0
   XMP only                               0                 0
   both present, keys differ              0                 0
   ```

   **The index reaches everything the union reaches**, so the ledger ∪ XMP union that #1 needs
   is unnecessary. The three files the index does not key are genuinely untagged and have been
   since June: `Kreayshawn - Gucci Gucci (Acapella)`,
   `Delta Heavy - White Flag (Instrumental - Full)` and its vocal.

7. **The index's key data is clean where the XMP's needs defending.** Of 1,789 `UKey` rows,
   **zero** files carry a tonic without a mode or a mode without a tonic, and **zero** carry two
   tonics or two modes. `HasPartialKey` has no cases against this source.

8. **The XMP is not converging and the index is.** `Mashup Samples`' XMP was last written
   2026-09-02 15:40 and is still 94 files behind seven days later. `Sample Staging`'s index was
   behind by three files on the evening of 2026-09-09 and had caught up within hours,
   unprompted.

9. **A replaced XMP is still read by Ableton.** `File.Replace` was measured on scratch files to
   **not** preserve the NTFS file id — the target keeps its name and takes on the replacement's
   id. So crateside's atomic write does re-identify the store, which is the mechanism ADR 0003
   worried about. It does not matter: the three stems crateside tagged at 19:37 through exactly
   that replace are now in the index, and the user confirmed they show in the browser. Ableton
   also rewrote that same XMP two hours later without clobbering a single tag crateside had put
   there.

## Decision

**The index is what crateside reads to learn what Ableton holds. The XMP stays what crateside
writes.**

`AbletonIndexReader` returns the same `AbletonFolderInfo` as `AbletonFolderInfoReader`, so the
two are interchangeable at the call site and the choice of source is a decision each caller
makes rather than a new vocabulary. The keyword interpretation moved to
`AbletonFileTags.FromKeywords`, since both stores spell keywords identically and there is no
reason for two copies of the rule that half a key is not a key.

**`Microsoft.Data.Sqlite` ships**, in `Crateside.Core`. EF Core earns its place when you own the
schema and write to it; here crateside owns neither and does neither.

**The database is discovered, never named.** `Live-files-*.db` in the index folder, taking the
most recently written — the one Live is actually using, which is the right signal when the
number is a schema version that an upgrade need not change and a stale file may sit beside the
live one.

**Read-only, unpooled, and never written.** Pooling is off because
`Microsoft.Data.Sqlite` keeps the file open after `Close()` since EF Core 6, and this file is
Ableton's.

## Consequences

**ADR 0011's `Untagged` rule can be un-deferred**, and 0011 is amended in place to say so. Its
`AuditRoot.Written` / `AuditRoot.Mirrored` distinction was the right call on the evidence it
had, but it turns out to describe **which store is stale**, not which root can be audited. Read
from the index, both roots are auditable and the honest finding count over `Mashup Samples` is
~~three~~ **zero**.

> **Corrected 2026-09-10 ([ADR 0017](0017-each-root-is-audited-from-the-store-that-holds-its-tags.md)).**
> "Three" was this ADR's own coverage number — the files the index does not key — reused as a
> finding count without being run. `Untagged` also requires a taggable verdict, and all three are
> unjudged, so they land in `NoVerdictCount` instead. Measured by running the switched audit:
> zero findings of any kind across both roots.

**Item #1's key-distribution panel no longer needs a union.** One source reaches 576 of 579.

**Nothing changes yet.** `TagAudit`, `StagingBoard` and the tagging stage still read the XMP.
Switching them is a separate change with its own test pass, and doing it inside this
investigation would have mixed a measurement with a migration.

## What is not settled

- **How long catch-up takes.** Measured only as "within hours" on one occasion. The next real
  track through the tagging stage gives this for free; it was not worth a synthetic write into a
  root the user works in, since the writer has no delete and would have left an orphan entry
  behind.
- **Whether a read-only open works with Live *not* running.** SQLite needs the `-shm` file to
  attach to a WAL database, and Ableton is always running on this machine. The reader degrades
  to an empty index rather than throwing, so the failure mode is safe but the case is untested.
- **Whether the index is ever simply wrong**, as against merely behind. Nothing measured here
  found a disagreement, but zero disagreements across 506 shared entries is evidence, not proof.

## Alternatives considered

**EF Core.** The user asked. Rejected: crateside neither owns this schema nor writes it, so a
model, a migration story and a change tracker would all be dead weight over four columns of a
read-only query.

**Read `keywords` instead of `metadata`.** `keywords` holds the same tags as numeric `keyw_id`s —
21,933 rows over 249 distinct ids — with no name table anywhere in the database, so the
vocabulary is presumably compiled into Live. Rejected: `metadata` gives the strings directly and
needs nothing decoded.

**Copy the database and read the copy.** Rejected on measurement: WAL means the `.db` alone is
stale, so a copy without `-wal` reads the wrong thing, and opening the real file read-only was
never a problem in the first place.

**Match a place by `places.name`.** Rejected: it is a display name. `files.parent_id` walks to
the drive root, so the real path is available and is what a `CratesidePaths` root can be
compared against.
