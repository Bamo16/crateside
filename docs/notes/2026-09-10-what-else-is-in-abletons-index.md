# What else is in Ableton's index

*Note, 2026-09-10, frozen. Surveyed read-only with Live running; moved here from
[#14](https://github.com/Bamo16/crateside/issues/14) on 2026-09-27. Counts are from the survey day.*

A survey of everything else in Ableton's index, now that [ADR 0013](../adr/0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md) established it is readable. Not a proposal — a listing, so the next person deciding what to build knows what is on the shelf and does not re-explore.

Surveyed 2026-09-10, read-only, Live running. Every table's row counts, every `metadata.key` decoded, and the columns of `files` inspected against the two real roots. Counts are from that day.

**Note the discipline.** Everything below is marked **measured** (I ran the query), **plausible** (the shape says so but nothing confirms it) or **unknown**. `use_count` in particular is the most interesting thing here *and* the least confirmed, and it would be easy to build something on a guess about what it counts.

---

## Worth having

### `files.use_count` — how often a sample has been reached for

**Measured.** Populated for all 579 files in `Mashup Samples`. Range 1–37, mean 6.38.

```
use_count   1  2  3  4  5  6  7  8  9 10 ... 30 37
files      86 74 60 57 55 46 31 31 25 22 ...  1  1
```

Top of the list: `Ludacris - Stand Up (Vocal - Full)` 37, `TLC - No Scrubs (Vocal - Full)` 30, `JAŸ-Z - Dirt Off Your Shoulder (Vocal - Full)` 29.

**Semantics unverified, and this matters.** Every single file has a count of at least 1, which is not what "used in a set" alone would produce for a library this size — so it plausibly counts previews or drags too, or starts at 1 when a file is first indexed. The three unkeyed files have the lowest counts in the library (1, 1, 2, mean 1.33 against 6.38 for the rest), which is suggestive but is three files.

**Cheap way to settle it:** note a file's count, preview it in the browser without using it, re-read; then drag it into a set and re-read again. Two reads answer it.

If it means what it looks like, it is the only signal in the system about **which samples the user actually uses** — nothing in the ledger, the XMP or StemForge's provenance knows this. That is a genuinely new axis: most-reached-for keys, samples never touched since separation, whether the key distribution of what gets *used* differs from what is *owned*.

### `files.file_size` and `files.mod_date`

**Measured.** Both populated everywhere. `Mashup Samples` is 12.17 GB across 579 files; `Sample Staging` 0.49 GB across 24. `mod_date` is unix seconds and tracks the file's mtime.

Cheap to report and needs no filesystem walk — the index already knows, so a size-by-key or growth-over-time panel costs one query rather than 579 `FileInfo` hits.

### `places.folder_kind` — generic root discovery

**Measured.** Seven places, and the kind tells you what each is:

```
kind  place              files
   0  Core Library        8295
   1  User Library          67
   2  Sample Staging        26
   2  Mashup Samples       581
   4  Current Project        0
   8  Built-in             758
  10  Plugins               41
```

**`folder_kind = 2` is "a folder the user added to the browser"** — which is exactly crateside's two roots and nothing else. `AbletonIndexReader` currently finds a place by matching the absolute path from `CratesidePaths`; `folder_kind` would let it discover the roots instead of being told them. **Plausible** that kind 2 means this in general rather than coincidentally here; two examples is not many.

### `Type|` coverage is not what the writer assumes

**Measured.** In `Mashup Samples`: `Type|Instrumental` 297, `Type|Vocal` 276. That is 573 against **576 keyed files — three keyed files carry no `Type|` at all**. And `Type|Loop`, which `AbletonFolderInfoWriter.Types` accepts, is used **nowhere in the library**.

Small, but it is the kind of gap the audit could name now that it can see the whole library.

### `vfolders` and `vfolder_patterns` — Ableton's own closed vocabulary

**Measured.** A two-table taxonomy: ~20 categories (`Bass`, `Brass`, `Pad`, `Percussive`, `Piano & Keys`, `Strings`, `Synth Lead`, `Ambient & Evolving`, …) and a pattern table mapping many spellings onto each (`Ambience`, `Ambient`, `Evolving`, `Ambient MPE` → `Ambient & Evolving`).

It is **instrument category, not genre**, and it covers Core Library content rather than the user's samples — so it does not answer #4/#5/#6 directly. It is worth reading anyway as **prior art from the vendor on exactly the problem those issues pose**: a closed vocabulary plus a synonym table that maps real-world naming onto it. If crateside is going to choose a genre vocabulary, this is the shape Ableton chose for the same job.

### `ancestors` — a closure table

**Measured.** 91,554 rows of `(file_id, ancestor_id)`. Makes "everything under this folder" a join rather than a recursive CTE. `AbletonIndexReader` uses a recursive CTE today and does not need changing — at 22 ms warm there is nothing to fix — but it is there if a query ever gets heavy.

---

## Present, and not about the user's samples

Listed so nobody spends an evening on them twice. All **measured** as having zero or near-zero rows for `Mashup Samples` and `Sample Staging`:

- **Device metadata** — `DvCI`, `DvCN`, `DvId`, `DvTy`, `DvNm`, `DvAN`, `DvVP`, plus `FoPr`/`FoDv`/`FoDE`/`FoHC`/`FoBt`/`FoTp` (factory packs), `Mx*` (Max for Live), `Pa*` (pack identity), `XF*`. Together most of the 43,472 `metadata` rows. All Core Library, Built-in and Plugins.
- **`Anno`** — 1,531 rows of preset blurbs ("Created by: …"). Core Library only.
- **`devices` / `file_devices`** — plugin identifiers and which files reference them. 4,341 rows for Core Library, 108 Built-in, 37 Plugins, **zero** for either sample root.
- **`fe_values`** — 268-byte blobs, **plausibly** feature-extraction summaries. 5,562 rows for Core Library but only **2** for `Mashup Samples`, so whatever it is, the user's samples do not have it. The real waveform data is the ~39 GB `Cache` folder beside the database, which is not this.
- **`keywords`** — the numeric mirror of `UKey`. 1,727 rows over 576 files in `Mashup Samples`, 18 distinct `keyw_id`s, **no name table anywhere in the database**, so the vocabulary is compiled into Live. `metadata` gives the same tags as strings. Two rows carry `is_auto = 1`; the rest are user-applied. Nothing here that `UKey` does not already give.

---

## Cannot be read

**`search_aggregation`** is an FTS4 virtual table declared `tokenize=AbletonTokenizer`. **Measured:** any query against it fails with

```
SQLite Error 1: 'unknown tokenizer: AbletonTokenizer'
```

The tokenizer is compiled into Live and is not available to any third-party reader. This does **not** affect anything else — opening the database, reading `sqlite_master` and querying every other table all work, and `AbletonIndexReader` never touches it. Worth recording because it looks like the obvious way to implement search (#9) and is a dead end.

---

## Not there at all

**Measured**, by decoding every `metadata.key` in the database and reading every column of `files`:

- **No BPM or tempo.** Nothing. Ableton keeps warp and tempo in the per-file `.asd` sidecar, not in this index — consistent with [ADR 0002](../adr/0002-there-is-no-warped-signal.md).
- **No date-added.** `mod_date` is the file's mtime and is the only date. [ADR 0008](../adr/0008-staged-since-is-the-files-creation-time.md) already had to solve this elsewhere; the index does not help.
- **No rating, no comment, no user-set color in use.** `files.colors` exists and is `1` for every file in both roots — Ableton's browser color labels, which the user has never used. Available if that ever changes.
- **`Current Project`** is a place (kind 4) and holds **zero** rows, so it does not offer "what is in the set I have open".

---

## Suggested next steps, in the order I would do them

1. **Settle what `use_count` counts.** Two reads and a drag. Everything interesting here depends on it, and building on the guess would be exactly the failure mode `CLAUDE.md` warns about.
2. **Name the three keyed files with no `Type|`**, once the audit reads the index. Falls out of work already planned.
3. **Consider `folder_kind = 2` for root discovery**, when there is a second reason to touch `AbletonIndexReader`. Not worth a change on its own.
4. **Read `vfolder_patterns` before settling the genre vocabulary** in #4. Prior art, not an answer.

## Blocked by

None - this is a record, not a build.
