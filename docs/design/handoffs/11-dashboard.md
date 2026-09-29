# Handoff 11: Counts becomes the Dashboard

Issue: #78 · built against `9077ac3`
Canvas: `Crateside Dashboard.dc.html` (1a the whole page at 2560px, 1b narrow at 1024px with
sections folded, 1c the all-three-wrong list opened and copied).

The canvas is a design reference in HTML. Port it to `Pages/Counts.razor` (rename it
`Dashboard.razor`) and its `.razor.css`. No new tokens and nothing newly coloured. Teal = the
machine is working, amber = your turn. Buttons are `.outline`, choices `.pill`. The header stays
`Bar`. Song names, live cards, stage failures, cancellations and activity rows are illustrative.
The weekly totals and all key-detector figures are the brief's measured numbers.

## 1. Decisions (the open questions)

1. **Not customizable.** A fixed order, four top-level sections, each foldable. Fold state is
   remembered per section (local storage is enough). No widget grid.
2. **The landing page, and the first tab.** Tabs: **Dashboard · Tracks · Tools · Settings**. `/`
   opens it, and the brand link in the header goes to it (it went to Tracks).
3. **Named Dashboard.** Route `/dashboard`. The user asked for **no** `/counts` or `/library`
   aliases: drop them.
4. **Live.** *Your turn* and *Working now* update as stages move, over the same push the Tracks
   header uses. *Over time* updates when a stage ends. *The library* is read on load and by
   **Re-read**, as today. The title row says when it was read.
5. **Links.** Listed per tile below. Every *Your turn* tile is a link. Nothing in *The library*
   is, apart from what it already has.

## 2. Page

`Bar` header, then the page body, `padding: 0 48px 56px`, grid `168px minmax(0,1fr)`, gap 48px.
**No width cap on the body**: the page uses the window. The inner sections cap themselves.

- **Title row**: `h2` Dashboard, then the lead: "{songs} songs filed, {n} finished this week.
  Updates as stages move." On the right, quiet (12.5px `--text-50`): "Library counts read
  {hh:mm} · Re-read" (Re-read is a text link, replacing the button).
- **Index** (sticky, 168px): the four sections at 13px/500, then the library's panels indented
  (22px, 12.5px/400). Counts beside them in mono `--text-40`. *Your turn*'s count is the sum of
  its tiles, in amber when non-zero. The panel in view is marked, as today.
- Sections are 40px apart. A section head is a `▾`/`▸` (mono 12px `--text-50`), the `h3`
  (16px/600), then a quiet gist. The whole head is the fold toggle.
- **Folded**, a section shows **one summary line** (13px `--text-60`, indented 24px) in place of
  its body, so the page still tells you what is going on when everything is folded. *The
  library* folds to its head alone, since the head already carries its totals.

## 3. Your turn

A grid of tiles, `repeat(auto-fill, minmax(300px,1fr))`, gap 12px. A tile is an `<a>` with a
`--surface` fill, `--line` border (lighter on hover), `padding: 16px 18px 14px`:
count (mono 34px/500, **amber**), label (14px/500), gist (12.5px `--text-60`), then the target
(12px `--text-50`, "Tracks · {filter} →").

| tile | count | goes to |
|---|---|---|
| In staging | ready to warp | Tracks, *In staging* |
| Needs attention | stopped, parked or with a finding | Tracks, *Needs attention* |
| Tags to answer | genre or familiarity | Tracks, *Tags to answer* |
| Tempo drifts | not warped, flagged by #75 | Tracks, a new *Tempo drifts* filter |
| Left in the playlist | failed removals | Settings, the removal row's failure list |
| Gave up | at any stage | Tracks, *Needs attention* |

A **zero** turns the count and label monochrome (`--text-35` / `--text-50`) and the gist says so
("none right now"). The tile stays so the grid never shifts. Folded summary: the non-zero counts
in amber, joined with " · ".

*Tempo drifts* needs a Tracks filter if #75 didn't add one. If that is too much for this pass,
link it to *In staging* and say so under "Differs from the design".

## 4. Working now

Five cards in one row, `repeat(5, minmax(0,1fr))`, gap 12px. Below 1800px, `repeat(auto-fill,
minmax(300px,1fr))`. Card: `--surface`, `--line` border, `padding: 14px 16px`, gap 8px, a caps
label (10.5px `--text-40`) on its label line.

- **Separation · GPU.** Label line with **Pause** / **Resume** (`.outline`, the same control as
  the Tracks header). Then the state chip (`StateChip`, teal filled with a dot while separating,
  neutral outline *Paused*), and the track name as a link to Track details. A 4px bar of elapsed
  against the ~6 min estimate: teal on teal 20% while running, `--text-35` on `--line` when
  paused. Status: "{m:ss} of about 6 min · {n} waiting, about {n×6} min". Idle: no chip or bar,
  "Nothing to separate." and the waiting count.
- **Judging · Claude.** The same anatomy. The canvas draws it paused: "Paused at {m:ss} · {n}
  waiting".
- **Quotas.** Two lines, each a name and a value in mono, a 4px monochrome bar (`--text-50` on
  `--line`), and "resets at {hh:mm}". Claude: percent of the 5-hour window. YouTube: "{used} of
  10,000" today. Quotas are neither working nor your turn, so no colour.
- **Background readers · unattended.** beatdetect with "{read} of {total}" and a teal bar, then
  genre lookups and familiarity lookups, each "{n} waiting" or *idle*.
- **Library backfill.** Label line with "Settings →". Value: "On · next in {n} min" or "Off".
  Then three mono counts: waiting · judged · gave up.

Folded summary: "Separating {track}, {n} waiting · Judging {state}, {n} waiting · Claude {p} % ·
beatdetect {read} of {total}". The word *Separating* in teal.

## 5. Over time

Three columns, `1fr 1fr 1.3fr`, gap 40px. Each has a caps label.

- **Tracks finished, by ISO week.** A column chart, 200px tall, one column per week from W34.
  Columns teal and scaled to the tallest, with the count above in mono and the week label
  below. **The current week** is teal 35% with a dashed teal border and labelled "W40 so far".
  Once there are more than 12 weeks, show the last 12.
- **Stages run.** A bar-table-style table without bars: Stage · Done · Failed · Cancelled for
  Separations, Verdicts, Handoffs. A non-zero Failed count opens those tracks on Tracks
  (*Needs attention*, filtered to that stage if the filter allows, or plain if not). Footnote:
  "About {lines} stage lines."
- **Latest activity.** The last 8 `ActivityLog` events: time (mono `--text-50`) · event
  (`--text-75`) · track, one line each and ellipsised. "The whole log →" opens the Activity log.

Folded summary: "{n} finished this week, {n} last week · last event {hh:mm} {event} {track}".

## 6. The library

Everything Counts holds, in a three-column grid, `repeat(3, minmax(0,1fr))`, gap 40px 48px,
under a `--line` rule. **Two columns below 2000px, one below 1400px.** The two detector panels
span two columns wherever there are two. Order and placement at three columns:

| row | col 1–2 | col 3 |
|---|---|---|
| 1 | Key detectors (span 2) | By key |
| 2 | By tempo | BPM detectors (span 2) |
| 3 | By genre · By familiarity + No key · By artist | |

Grid order: Key detectors, By key, By tempo, BPM detectors, By genre, By familiarity (with No
key under it), By artist. `grid-auto-flow: dense` lets row 2's two-column panel sit beside tempo.
Panels are unchanged from handoff 08 as built, apart from these:

- **By tempo** keeps the shape line (*Steady · Mostly steady · Drifting*) and the beatdetect
  line under it. The *Shared by 10 or more* column ellipsises at this width.
- **Detectors against the warps** is renamed **BPM detectors against the warps**. At two columns
  the band table and the misses strip sit side by side (1fr 1fr), with the strip's label column
  at 110px.
- **What the sources called the rest** is the dashed opener under By genre, closed by default.

### Key detectors (new)

Head: `h3` Key detectors, gist "against Claude's verdict, latest run per song". On the right, two
`.pill`s: **HIGH or MEDIUM 397** (default) · **HIGH only 192**. They switch the accuracy table,
its lead and the all-three-wrong count.

Lead (13.5px `--text-75`): "On 397 HIGH or MEDIUM verdicts, keydetect is right on 73 %, and
right or a near miss (a fifth or the relative key) on 88 %. The pale part of a bar is those near
misses, which still pair fine for a mashup." For HIGH only: "On the 192 HIGH verdicts, keydetect
is right on 78 %…".

Two tables side by side (1.25fr 1fr, gap 40px):

**Accuracy**: Detector (name in mono, with a gist under it: *Fil's ffmpeg filter*, *python*) · a
**split bar** (the tempo panel's house style: the full bar is right or close at teal 35%, and the
solid part from the left is right) · Right % · Close %.

| | right | right or close |
|---|---|---|
| keydetect | 73 % (290) | 88 % (351) |
| crema | 65 % (259) | 81 % (321) |
| librosa | 59 % (234) | 76 % (303) |

Close = right + fifth + relative. With **HIGH only**, the bar is solid only (78 / 71 / 62 %),
Close shows —, and the footnote reads "Near misses are measured on HIGH or MEDIUM only."

**How the misses relate to the verdict**: rows *Fifth, up or down* · *Relative major or minor* ·
**Close** (subtotal) · *Parallel major or minor* · *Anything else* · *No reading* · **All misses**
(total). Columns keydetect · crema · librosa. Subtotal rows are 500 weight on a `rgba(255,255,255,.03)`
band with a `--line-strong` rule. Numbers are the brief's. Footnote: "Measured on HIGH or MEDIUM
verdicts, {date}. Parallel is not counted as close." (It isn't a pairing a mashup can use as
is.) Should the parallel count as close, fold it into the subtotal; the user can decide.

**All three wrong**, a `--surface` band under the tables: "{30} songs all three detectors got
wrong" (14px/500), then "Only the verdict has the key. Test cases for keydetect's corpus."
Buttons right: **Show the songs** (toggles to *Hide the songs*) · **Copy as text**. Open, it
lists Song · Verdict · Conf. · keydetect · crema · librosa (the verdict in mono 500, the
readings `--text-60`), 6 rows then "{n} more · show all" (1c).

**Copy as text** copies **all** the songs in the current pill's set, open or not, as tab-separated
lines with a header row: `song  verdict  confidence  keydetect  crema  librosa`. A missing reading
is `no reading`. The button reads *Copied* for two seconds.

## 7. Narrow (1b)

Below 1100px, as Counts today: the index becomes a wrapping row of links under the title, and
the grid is one column. *Your turn* tiles become compact rows (count 24px beside label and gist,
`minmax(280px,1fr)`). *Working now* cards wrap two to a row. *Over time* stacks.

## 8. After building

Update `docs/design/DESIGN.md`:

- Rename **Counts** to **Dashboard**, the landing page and first tab. Record the four sections,
  the fold rule (summary line), and the library grid breakpoints.
- **Tiles**: an amber count is a link to the filter it counts. Zero goes monochrome, and the
  tile stays.
- **Split bars** are now used by tempo and key detectors: solid = the strict count, teal 35% =
  the looser one.
- **Page headers** table: Dashboard, Bar. **Brand link** goes to Dashboard.
- Clear "Counts (handoff 08)" under *Differs from the design* where this supersedes it. The
  detector-band and shape-line notes still hold, under the new page name.

Index handoff 11 in `docs/design/ARCHIVE.md`.
