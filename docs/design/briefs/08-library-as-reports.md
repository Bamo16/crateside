# crateside: the Library page becomes a reports page

Issue: #68 (with #60 and #56)

For a Claude Design session. The Library page is gaining panels, and the user sees it turning into
a reports page more than a "library" one. Three additions are wanted, and the page needs an
organisation that holds them and whatever comes after. The implementing agent ports your design to
Blazor, so send layouts and states, not code.

Built against `e6c3802`. The rules as built are in `docs/design/DESIGN.md`. The page today is
`src/Crateside/Components/Pages/Library.razor` (with its `.razor.css`). Your canvases are in
`docs/design/canvases/`.

**What this page is for, in the user's words:** a fun thing to look at. Nobody makes an important
decision from it. Keep it light and legible, not a dashboard.

---

## What the page holds today

Top to bottom, under the *Bar* page header:

1. A title block: *Library*, a subtitle, and a *Re-read* button.
2. A summary line: how many tracks carry a key, across how many of the 24 keys, and the file count.
3. Up to two notices in prose: never-judged uploads (with a *Queue them for judging* button), and
   keys not backed by Ableton's tag store.
4. **By key:** a table of the 24 keys, one row each: key, Camelot code, a horizontal bar, a count.
   One count per track, however many samples it has.
5. **By genre:** the same shape for the seven genres, then *What the sources called the rest*. Thin
   today because few genres are settled.
6. **Files with no key:** a list.

## Three additions

### 1. Tempo: the library by BPM (#68)

Every warped track now has a **true BPM**, read from the warp the user set by ear (ADR 0038). Only
warped BPMs go in this view. The user does not want detector BPMs here: they are too far off to be
worth looking at.

Measured 2026-09-27 over the library: **451 songs, all warped.** 288 hold a whole-number BPM (the
rest are real non-whole tempos such as 95.41, mostly 80s and 90s recordings). By 10 BPM:

```
 70-79     7    120-129   92    170-179   53
 80-89    28    130-139   48    180-189    8
 90-99    55    140-149   38    190-199    4
100-109   46    150-159   25    200-219    2
110-119   25    160-169   20
```

The most common tempos: 174 (26 songs: drum and bass), 128 (16), 130 (15), 126 (14), 125 (12),
150 (12), 120 (11), 100 (10).

Each warp also has a **shape**: **steady** 392, **steady with changes** 23 (a tempo change in the
intro or outro, or a second stable section), **unsteady** 36 (live drummers; the BPM is then a
median with a range). BPMs are shown as whole numbers; decimals are not wanted in the UI.

Design: how the distribution reads (the bins, whether the common tempos stand out, whether shape
shows here at all).

### 2. Detector accuracy: how close the detectors come to the warps (#68)

The warped BPM is ground truth, so the page can show how often each detector gets it right. The
user can take this to the maintainer of ffmpeg's `beatdetect` filter, a friend.

Measured 2026-09-26, on the 332 songs with both a warp and a keytag run:

| detector | within 0.5 BPM | within 2 | half or double | wrong |
|---|---|---|---|---|
| keytag's recommended BPM | 205 | (the rest split between these) | | 61 |
| raw `beatdetect` | 124 | | | 129 |

`beatdetect`'s misses cluster at about **2/3 and 3/4 of the true tempo**, a triplet or dotted
subdivision: 174 read as 116, 128 as 85, 150 as 112. That pattern is the finding worth seeing.

Design: a panel that shows each detector's hit rate in bands (exact, close, half/double, 2/3 or
3/4, wrong), and makes the 2/3 and 3/4 cluster visible. Possibly a way to see which songs a
detector missed, since the user may want to hand a list to the filter's maintainer.

### 3. Artists: the library by artist (#60)

Tracks counted by artist, limited to the ones with enough songs to be interesting. Counted by
**primary artist**, the first credited name, as genre learning already does (ADR 0033).

Measured 2026-09-27: 344 artists across 451 songs; **20 have 3 or more songs**, 4 have 5 or more.
The top: Ludacris 12, Fox Stevenson 9, Snail's House 7, PSYQUI 6, then 1991, Aiobahn, Kreayshawn,
Lil Wayne, Porter Robinson and S3RL on 4, then 50 Cent and DMX on 3.

Design: how the artist view reads, what cut-off it uses (top N, or a minimum count), and whether it
shows anything beyond the count (their keys, their BPMs).

## The page as a whole (#56)

- **Its name and tab label.** "Library" may no longer fit. The tab row today: Tracks · Library ·
  Partners · Settings.
- **How its panels are organised** as more arrive: one long page, sections with a small table of
  contents, tabs within the page, or something else. Key and genre tables exist; tempo, detector
  accuracy and artists join them; familiarity (Mainstream or Obscure) is another axis the page could
  count the same way.
- **Where the prose notices go** (never-judged uploads, unbacked keys). They are actions, not
  reports.

## Constraints

- No new tokens, and nothing newly coloured (`DESIGN.md`). Amber is still only *your turn*, and the
  page is rarely anyone's turn.
- The existing bar-table style (key and genre) is the house style for counts. Reuse it where it fits.
- Buttons are the existing `.outline` / `.primary` / `.danger`.
- Counts are one per track (song), not per file, as the key table already is.

## What to send back

A zip holding the handoff (`08-library-as-reports.md`), the canvas, `support.js` only if it changed,
and a README saying where each goes. The link route (`docs/agents/design.md`, handoff 06c) is
preferred.
