# Handoff 21: Activity, a dashboard section of its own

Brief 21, issue #189 · built against `43e9ef0`
Canvas: `Crateside Activity Panel.dc.html`, live. **1a** is the dashboard at 2000px at 2:14 PM,
with the section in place: the stage filters, *Failed or canceled*, the page size, the pager and
opening a result all work. **1b** shows each state a row can be in, and a long result closed and
opened. **1c** shows the narrow layout, a later page when new rows arrive, an empty filter, and the
folded summary line.

The rows are real `jobs.jsonl` records from 2026-11-08 and real tracks from the source index, with
the times shifted to one afternoon. The running texts (*1 of 3 files written*, *turn 4 on
deepseek-web*), the judge line's token tail, the older generated rows and every count are invented.

**The rule:** one row per action, joined on the job id and ordered by when it started, in a table
with fixed columns, so the track name always starts at the same x. Nothing is newly colored: teal
while running, green for a recent finish, red for a failure.

## 1. Where it sits

- **A fifth dashboard section, third in order**: *Your turn*, *Working now*, **Activity**, *Over
  time*, *The library*. It folds like the others, and the fold goes in the same cookie.
- **The index** gains *Activity* between *Working now* and *Over time*. While anything runs, its
  count is the number running, in teal mono, followed by `●`; otherwise no count.
- **Width**: the content column, capped by a new `--table-activity: 97.5rem` (1560px). Add it to
  `DESIGN.md`'s table caps.
- **Over time** loses *Latest activity* and becomes two equal columns (`repeat(2, minmax(0,1fr))`,
  gap 48px) under the same cap: the weeks chart and *Stages run*, otherwise unchanged.
- **The head**: `▾ Activity` and the gist (13, `--text-60`): *live · 2 running · 9 finished in the
  last 10 minutes · last at 2:11 PM*. *2 running* is mono teal; with none running the gist starts
  *live · idle*.

## 2. The filters

One row above the table, wrapping:

- `STAGE` (caps label), then pills: **All**, **Download**, **Detection**, **Judging**,
  **Separation**, **Handoff**, **Tagging**. Each carries its count in mono 11 at 70% after the
  label. The count is of everything on record, not the page.
- Stages **combine** (several on at once); *All* is on when none is, and clicking it clears them.
  On is `.pill.on.plain`, off is `.pill.off`. Each pill has `aria-pressed`.
- A 1px `--line` divider, then **Failed or canceled**, a toggle that narrows whatever stages are on.
  On, it is a red filled pill (`.pill.on.red`); off, `.pill.off`.
- A quiet hint after them (12, `--text-40`): *Pick several stages; All clears them. Remembered with
  the folds.*
- **Remembered**: the stage set, *Failed or canceled* and the page size go in the dashboard cookie,
  so the server renders them. The page number is not remembered.
- Changing any filter or the page size goes back to page 1.

Stage names map to `JobStage`: Download → `Download`, Detection → `Evidence`, Judging → `Verdict`,
Separation → `Separation`, Handoff → `Handoff`, Tagging → `Tagging`. The brief also named *warp*; no
job stage writes warps, so there is no pill for it. If warp writes become jobs, they get a pill.

## 3. The table

A box: `--surface`, 1px `--line-strong`, radius 8, overflow hidden.

**Columns**, one grid for the header, every row and the opened detail:
`16px 72px 92px 300px minmax(0,1fr) 92px`, column gap 14, padding 0 14px.

| column | head | content |
|---|---|---|
| status | (none) | the glyph (below), centered |
| Started | `STARTED` | the start time to the minute, locale `"t"`, mono 12 `--text-50`. `title`: *4 min ago* |
| Stage | `STAGE` | the stage name, 13 `--text-60`, no wrap |
| Track | `TRACK` | `Artist – Title` as a link to Track details, 13/500 `--text`, ellipsis at 300px, `title` the whole name |
| Result | `RESULT · CLICK FOR ALL OF IT` | the result, one line with an ellipsis, then a `▸` (mono 11 `--text-30`) |
| Took | `TOOK` (right) | the duration, mono 12 `--text-50`, right-aligned, never truncated |

The header row is 30px on `--text` at 2%, caps labels. Each row is at least 38px with a `--line`
bottom border; hover adds `--text` at 2.5%.

**Order**: newest *start* first. A row is created when its stage starts and is updated in place
when it finishes, so it never moves. (The brief's question 3, *the most recent thing*, is answered
by the gist's *last at* and by the green checks.)

**Buckets**: a divider row before the first row of each: **Last 10 minutes** (`since 2:04 PM`),
**Earlier today** (`before 2:04 PM`), **Yesterday**, then the date (*Sat 7 Nov*). Caps label, then
the range in mono 11.5 `--text-35`, padding 14 14 6, a `--line-strong` bottom border. Times stay
clock times: relative time is only in the `title` and in the bucket.

**Runs of one track**: when a row's track is the same as the row above it (across stages, and
across any filter), the track column shows no name, only a 1px rule (`--text` at 18%), 5px in,
full row height. A bucket divider and the top of a page start the name again. The rule cell keeps
the name in `aria-label`.

### Row states (1b)

| state | glyph | row | result | Took |
|---|---|---|---|---|
| running | teal 8px dot, pulsing (1.6 s, opacity 1 → .35; none under reduced motion) | teal 5% wash (8% on hover) | what it is doing + ` …`, `--text-60` | time so far + ` so far`, teal, ticking |
| finished, in the last 10 minutes | `✓` green, mono 12 | plain | the summary, `--text-75` | duration |
| finished, earlier | `✓` `--text-30` | plain | the summary | duration |
| failed | `✕` red, 600 | plain | `failed: ` + the error, red | duration |
| canceled | `–` `--text-40` | plain | `canceled: ` + the reason, `--text-50` | duration |

- **Running text**: what the app already knows. A separation: *1 of 3 files written* from
  `expectedOutputs` and the files on disk. A judging session: *turn 4 on deepseek-web* if the turn
  is known. Anything else: *running*. If none of it is cheap, *running* everywhere is fine.
- **The green check lasts while the row is in Last 10 minutes**, then turns grey. There is no
  other highlight and no fade.
- **Failures stay red** for as long as they are listed.
- **The result text is the job's `summary`** (for a failure, its `error`), exactly the wording
  Track details' Activity log shows, minus that log's ` · 15 s` tail, which is *Took* here. No
  rewording.

### Opening a row

The result cell is a `<button>` (`aria-expanded`). Clicking it opens a detail block under the row,
on `--text` at 2%, its body spanning the Result and Took columns:

1. The whole text, 13/1.55, wrapping; `--text`, red for a failure.
2. A meta line in mono 11.5 `--text-40`: `started 2:07:33 PM · finished 2:10:54 PM
   · 3 min 21 s · n3CvFYuyTLQ:Separation` (*running 3 min 18 s* while running; *failed* /
   *canceled* in place of *finished*). Times here carry seconds.
3. Links (12.5): **Track details →** and **Its activity log →** (`--text-60`, to `#activity`).

Several rows can be open at once; `▸` turns `▾`. The full text is also the button's `title`.

### Truncation

The result ends in an ellipsis inside its column; nothing else shrinks for it. The judge line is
the longest: *B minor, LOW, 0 sections; 3 turns on sonnet, 22 s, 41k in (38k cached) · 2.1k out*
fits at 1560px and truncates below about 1400px. A download error with yt-dlp's full message
truncates at most widths. Both read whole when opened.

## 4. More

A footer bar in the box: padding 10 14, top border `--line-strong`, `--text` at 2%, 12.5
`--text-60`, wrapping.

- Left: `1–12 of 4,136`, mono.
- **Per page**: pills `12` `25` `50` `100` (default 12), the current one on.
- Right: `‹ Newer`, page numbers (the first three, the last, and the current ± 1, with `…`
  between; `aria-current="page"` on the current), `Older ›`. Newer is disabled on page 1, Older on
  the last.
- **Page 1 is live**: a row that starts appears at the top. **A later page holds still**: new rows
  do not shift it. Instead a line appears at the top of the box (12.5, teal, centered, a button):
  *↑ 2 new since you paged away · back to page 1*.
- **Empty** (filters match nothing): one line in the box, 13 `--text-50`, centered: *Nothing with
  these filters yet.* followed by an underlined **Clear them** that resets the filters.

## 5. Narrow (1c)

Below 1100px of content width the grid gives way to a stack per row (padding 10 14):

1. Top line: status glyph, *Started*, *Stage* (12.5), then *Took* pushed right.
2. The track link, 500, one line with an ellipsis, only on the first row of a run.
3. The result, 13/1.45, clamped to two lines.

The running wash stays. The filters wrap above the box. The pager keeps *‹ Newer* and *Older ›*
and drops the page numbers.

## 6. Folded

The one summary line after `▸ Activity`: *2 running* (teal) · *last finished 2:11 PM, Detection,
The Prototypes, Lowes – Reason* · *1 failed* today (red, a link that unfolds the section with
*Failed or canceled* on). The failed part only when there is one today; with nothing running it
starts *idle*.

## What this replaces

- *Latest activity* in `OverTime.razor` and its eight two-line entries.
- `DESIGN.md`'s *Dashboard*: "Four sections" becomes five; *Over time* drops "the latest eight
  stage events"; the *Differs* entry "No 'The whole log →' link" is answered by the pager and
  filters here (still no separate page).

## Open for the build

None of the design is open. If `jobs.jsonl` cannot be paged cheaply from the newest end, read it
once into memory as the dashboard does for *Stages run*; 4,000 actions is small.
