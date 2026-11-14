# Handoff 08: Library becomes Counts

Issues: #68, #60, #56 · answers `briefs/08-library-as-reports.md` · built against `e6c3802`
Canvas: `Crateside Counts Page.dc.html` (1a whole page, 1b a detector band opened).

The canvas is a design reference in HTML. Port it to `Pages/Library.razor` and its `.razor.css`.
No new tokens, nothing newly colored, buttons stay `.outline` / `.primary` / `.danger`.
Song names, per-song figures, and the key, genre and familiarity spreads are illustrative. The
tempo bins, common tempos, warp shapes, artist counts and detector totals are the brief's measured
figures.

## 1. Name and tab

- Tab label and page title: **Counts**. Tab row: Tracks · Counts · Partners · Settings.
  `<PageTitle>Counts · crateside</PageTitle>`. Keep the `Bar` header shape.
- Route: add `/counts`, keep `/library` as a second `@page` so old links work.
- Rename the `BrandNav.Tab.Library` member only if cheap; the label is what matters.
- Why: the page counts things, one per song. "Library" promised a place to browse files;
  "Reports" or "Stats" promise a dashboard, which the user doesn't want.

## 2. Page organization

- One long page. No in-page tabs: every panel is short, and scrolling is the "fun to look at" part.
- Top to bottom: title block → action band (only when there is one) → a two-column grid:
  **index** (168px, sticky, `top: 24px`) and **panels** (`minmax(0, var(--table-library))`), gap 48px.
- Panel order: **By key → By tempo → By genre → By familiarity → By artist → Detectors against
  the warps → N files with no key**. Always-full counts first, slow-filling ones next, the study
  and the leftovers last.
- **Index**: one link per panel, label left, a mono count right (`--text-40`): keys 24, songs 451,
  genres 7, familiarity 2, artists shown, detector songs, unkeyed files. The panel in view gets
  `--surface` background and `--text`; others `--text-60`. Hover as in view. Drop a link whose
  panel is not drawn. Below 1100px viewport the grid is one column and the index becomes a
  wrapping flex row of the same links under the title (not sticky).
- Panel head: `h3` 16px/600, then an optional one-line lead in `--text-75`, 13.5px, max 72ch.
- Panels 44px apart.
- **Summary line** moves into the subtitle: "What is finished and filed, counted one per song
  however many samples it has. 451 songs, 438 with a key across 24 of 24 keys, 1,612 files."

## 3. The notices

- **Never judged** is an action, so it leaves the report flow. It becomes a band between the title
  block and the grid, full width of the grid (1112px cap), `--surface`, 1px `--line-strong`,
  radius 8px, 14px 16px padding, in the side stack's status + action shape:
  label line (*Never judged*, `.stack-label` style) with **Queue them for judging** (`.outline`)
  right; the value ("14 uploads tagged by hand before crateside"); the status prose (what queuing
  does). Not drawn when there are none. After queuing, the value reads "Queued 14 for judging"
  and the button goes.
- **Unbacked keys** is not the user's to act on (crateside backs them). It becomes a one-sentence
  footnote under the By key table, `--text-60` 13px, no border, no amber. Drop the current
  left-border box. Not drawn when zero.

## 4. By key

Unchanged table. Show the top 8 rows, then "Showing the top 8 · **all 24 keys**" (a text link
that expands in place, and "fewer" to fold). Empty keys stay in the full list as today.

## 5. By tempo (new, #68)

- Source: the warp's true BPM only (ADR 0038). Songs without a warp are not counted and not
  mentioned; detector BPMs never appear here.
- Rounded to a whole number for binning and display. No decimals.
- Bins of 10 BPM from 70 (`70–79` … `190–199`), with the empty high end merged into one last
  bin (`200–219` today: merge any trailing bins under 3 songs). Below 70 likewise merged.
  Ordered by tempo, not count: this is a distribution, and the shape is the point.
- Columns: **BPM** (mono) · **Songs** (bar) · **Count** · **Shared by 10 or more**.
- The bar is the bin total in teal at 35% alpha; inside it, from the left, a solid teal segment
  for the songs on tempos that 10 or more songs share. That makes 174 (drum and bass) and the
  120s visibly stand out without a second chart.
- The last column lists those tempos, mono, `--text-75`: `128 ×16  126 ×14  125 ×12  120 ×11`.
  The threshold is 10 songs; below it the cell is empty.
- **Shape** is one line under the table, not in the bins: "Steady 392 · Steady with changes 23 ·
  Unsteady 36, counted at their median". Numbers mono `--text`, words `--text-60`.

## 6. By genre

Unchanged, including *What the sources called the rest*. The lead sentence becomes
"N songs carry no genre yet: nobody has settled one." plus a link **Answer them on Tracks**
that opens Tracks with the *Tags to answer* filter on.

## 7. By familiarity (new)

Same shape as genre: rows **Mainstream**, **Obscure**, **Not answered**. The last is drawn in
the empty-bucket style (`--text-50`, bar `--line-strong`). A derived value counts as answered,
as on the side stack.

## 8. By artist (new, #60)

- Primary artist (first credited name, ADR 0033). Cut-off: **3 or more songs** (20 today).
  A minimum, not a top N, so the list grows with the library honestly. Lead: "Artists with three
  or more songs, by first credited name: 20 of 344. The rest have one or two each."
- Columns: **Artist** (ellipsis at 170px) · **Songs** (bar) · **Count** · **Keys** · **BPM**.
- **Keys**: their distinct Camelot codes, most common first, up to 3, mono `--text-60`.
- **BPM**: min–max of their warped BPMs, whole numbers, mono `--text-60`; one song's worth shows
  a single number. These two are what makes the table useful for mashups.
- Sorted by count, then name. Show 12, then "Showing 12 of 20 · **all 20**".

## 9. Detectors against the warps (new, #68)

### The table (1a)

- Songs with both a warp and a keytag run (332 today). Lead states that number.
- Rows are bands, columns are detectors: **keytag, recommended** and **beatdetect, raw**. Each cell
  is a bar plus a count (40px mono). Bars share one scale (the largest cell), so the two
  detectors compare directly.
- Bands, first match wins, compared on whole-number BPMs is fine but test on the real values:
  1. **Exact**: within 0.5.
  2. **Close**: within 2.
  3. **Half or double**: detected within 2 of ×½ or ×2.
  4. **Two-thirds or three-quarters**: within 2 of ×2/3 or ×3/4. (Include ×4/3 and ×3/2 here too
     if they occur; label then reads "Triplet or dotted".)
  5. **Wrong**: anything else.
- Each row carries its rule under the name in mono 11.5px `--text-50` (`within 0.5`,
  `×½ · ×2 · ±2`, …).
- The two-thirds/three-quarters row is the finding: its name is 600 weight and the row has a
  `rgba(255,255,255,.03)` wash. No color.

### Where the misses land (1a)

- Under the table, one strip per detector: a column chart of detected ÷ warped for every
  non-exact song, 64 bins on a log₂ axis from 0.4 to 2.2, 64px tall, columns teal, 1px gap.
- Axis ticks at ½, 2/3, 3/4, 1, 4/3, 3/2, 2 (mono 11.5px). 2/3 and 3/4 in `--text`, the rest
  `--text-50`.
- Both strips share one vertical scale, so beatdetect's spikes at 2/3 and 3/4 read against
  keytag's flat strip at a glance. Header: "Where the misses land · detected ÷ warped", with
  "exact matches left out" on the right.

### A band's songs (1b)

- Every count in the table is a button (underline on hover). Clicking opens that band × detector
  under the row, indented to the first data column: a `--surface` box, radius 8px.
- Label line: "beatdetect · 88 songs read at two-thirds or three-quarters", then **Copy as text**
  (`.outline`) and **Close** (`.outline`).
- List: Song · Warp · Read · Ratio (ratio 2 decimals, the one place a decimal is allowed, since it
  is a ratio, not a BPM). Sorted by ratio then song. Show 8, then "N more · show all".
- **Copy as text** writes plain lines `Artist - Title · 174 · 116 · 0.67`, one per song, with a
  first line naming the detector and band. This is the list for the filter's maintainer.
- One open at a time; opening another closes the first.

## 10. Files with no key

Unchanged content, but closed by default behind a dashed opener ("▸ Show the files", with
"Ableton holds no key for these either" right, `--text-50`), the sample list's opener style.

## DESIGN.md

After building, update:
- **Page headers** table: the Library row becomes **Counts · Bar**.
- A short **Counts page** section: long page + sticky index, panel order, the action band as the
  one place for actions, the "top N · all" fold, the solid-segment bar for tempo peaks.
- Note the key table's minor/major coloring (teal/amber bars) as a known exception it already
  is: new bar tables are teal only.

## Not done here

- No per-artist or per-tempo drill-down to Tracks beyond the genre link. Possible later.
- Detector accuracy by tempo range (does beatdetect fail more at 170+?) would be a good follow-up
  if the maintainer asks.
