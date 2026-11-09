# crateside: the dashboard's activity panel

Issue: #189

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

This brief was written against the commit that adds it. The rules as built are in
`docs/design/DESIGN.md` (*Dashboard*); the page's canvas is *Crateside Dashboard* in
`docs/design/canvases/`. Today the panel is the *Latest activity* column of
`src/Crateside/Components/Dashboard/OverTime.razor`, beside *Tracks finished, by ISO week* and
*Stages run*. The per-track log is `src/Crateside/Components/ActivityLog.razor` on Track details.

Screenshots in [`21-activity/`](21-activity/):

- `dashboard-today.png`: the whole dashboard at 2000 px wide. *Latest activity* is the right third
  of the *Over time* row.
- `latest-activity-today.png`: the panel itself, 8 lines, all for one track.
- `track-page-with-activity.png`: Track details for the same track; its *Activity* log at the
  bottom is the format the user likes.

---

## Why

The user, 2026-11-09: the activity panel is one of the most useful things on the dashboard. He
glances at it to answer three questions:

1. What is going on right this second?
2. What has happened in the last 5 to 10 minutes?
3. What was the most recent thing that happened?

and clicks an entry for detail. He is willing to give it more space if that buys more or better
information.

## What is wrong today

- **Two lines per action.** Every stage writes *download started* and then *downloaded*, so 8 lines
  show 4 actions.
- **The track name wanders.** It follows the verb, so its left edge moves line by line
  (*separated* / *separation started* / *judged* …) and the column is hard to scan.
- **Repetition.** When every line is the same track, the name repeats 8 times. Lines can also be
  for 8 different tracks, so the name can't simply be dropped.
- **It says less than the track page.** Track details' Activity log gives each action's result in
  a short line: *downloaded flac 258 kbps (format 141) · 15 s*, *keydetect B major (r 0.83),
  madmom B minor, edma B minor · 3 s*, *B minor, LOW, 0 sections; 3 turns on sonnet, 22 s* (tokens
  now follow that), *cut vocal_full, instrumental_full and a drum guide (3 files) · 3 min*. The
  dashboard shows only the verb.
- **Small.** One third of one row, beside two charts, though the page has room.
- **Only the last 8.** He hasn't needed more often, but wants a way to see more.

## What the user asked for

- **Each entry names its track as a link**, with room for it.
- **One entry per action**: shown while running (some sign it is still working) and replaced in
  place by its result when it finishes. A failure reads as a failure.
- **Reuse the track page's wording** for the result. Those lines are already concise; decide how
  a long one truncates (it can be longer than the space: a verdict line, a download with format
  details).
- **More than 8 when wanted**: e.g. a paged list with a choice of entries per page, **filterable by
  event type** (download, detection, judging, separation, handoff/stage & tag, warp, … — the stages
  the app runs). Filter by type matters most to him.
- **More width.** It needn't span the page, but the page has space to give.

## Facts the design can rely on

- Every stage writes a *started* record and a *finished* record (Succeeded, Failed, Canceled) with
  the same job id (`<trackKey>:<Stage>`), so an action's two records can be joined into one entry.
- Each finished record carries the result line the track page shows (`summary`), as text.
  The dashboard can show the same text.
- Entries carry a timestamp; today the panel shows local time to the minute.
- Activity is live (the dashboard updates as stages move).
- A track's name comes from its key (a YouTube id or a file key), resolved to *Artist - Title*.

## Open questions for you to settle

- Where the panel goes and how much of the page it takes, and what moves to make room.
- How runs of entries for one track read (grouped under the track, or the name dimmed when it
  repeats, or something else), given a mix of tracks is just as common.
- How "running" shows, how long a finished entry keeps any highlight, and how a failure stands out.
- The control for more: paging and page size, or a "show more" that grows in place, and where the
  type filter sits. Whether the filter is remembered.
- Whether times go relative (*2 min ago*) or stay clock times, given question 2 above.
