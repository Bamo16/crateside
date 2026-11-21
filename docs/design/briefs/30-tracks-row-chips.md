# crateside: Tracks rows with a fixed notes column, fewer chips, and use of wide screens

Issue: #226 (Tracks rows: which chips earn their place, aligned, and wide screens)

For Bamo to review, then one build agent (Sonnet, small). No Claude Design round: the row keeps its
look, only the grid changes. Related: #225 (the "added by URL" chip is gone, landed).
Files: `Components/Pages/Tracks.razor` (row markup, lines 440-520), `Tracks.razor.css` (`.row` grid,
`.flag`), `wwwroot/app.css` (`--table-tracks`).

## Problem

The flags sit inline after the title, so a long title pushes them right or off the row, and the
chips never line up (see `30-tracks-row/before-drifts-2560.png`: the "drifts" flags start at a
different x on every row). At 2560 px the table stops at 92rem (1472 px) and the right 40% of the
screen is empty. At 1280 px the title column is the only flexible one, so every added column costs
title room.

## Inventory (710 rows today, read from `/api/tracks?filter=All`)

| What | Text | Shows when | Rows | What Bamo does about it | Decision |
|---|---|---|---|---|---|
| Cover, title | name | always | 710 | reads, opens details | **keep** |
| Key | `D min` / `—` | always; `—` when no key | 645 with a key | parked "no key" tracks need a judge | **keep** |
| Mode badge | `Dor`, `Phr`, `Lyd`, `Mixo`, `Loc`, `maj/min` | verdict is modal or mixed | 28 | tells him the tag is not plain minor/major | **keep**, inside the key cell |
| Confidence | `HIGH` ... `WITHHELD` | always | 710 | sorts out Low/Withheld | **keep** |
| BPM | `~97`, `158`, italic for estimate | always | 710 (151 with `~`, 10 estimates) | the number; `~` means not vouched by a warp | **keep** |
| Off-tempo pennant | hollow or filled mark after BPM | the warp drifts off its grid | 22 | opens Off its tempo, trues up | **keep**, already aligned in the BPM cell |
| Genre | one or two names | always; blank if none | 645 | corrects it in details | **keep** |
| State chip | `done`, `ready to warp`, `parked · no key` ... | always | 710 (585 done, 96 ready, 29 parked or short) | the whole workflow | **keep** |
| Staged since | `staged 3 days ago` | only the In staging view | 97 | knows what has sat too long | **keep**, view-only column as today |
| Tempo flag | `drifts 4%`, `changes tempo`, `part changes tempo` | the warp's tempo is not steady | 84 (52 + 14 + 18) | looks at the warp, may split or fix | **keep**, in the notes column |
| Auto warp | `Auto warp` | a stem's warp is crateside's guess | 14 | saves a real warp | **keep**, in the notes column |
| Vocals near-silent | `vocals near-silent` + a button | vocal stem far quieter than instrumental | 2 | switches to Instrumental | **keep the chip** in the notes column; **move the "Switch to Instrumental" button to Track details** (it makes the row 150 px wider and breaks the grid; the stem list is where he acts on stems) |
| Not renamed | `2 files not renamed` | files in staging still carry the old name | 0 | runs Rename from tags | **keep**, notes column (it asks for an action) |
| Stale key tags | `Ableton's key on these files is not the current verdict.` | stale tagged stems | 0 | retags | **keep**, notes column, shortened to `stale key tags`; the sentence goes in the tooltip |
| Name pending | `name pending` | StemForge has not named it yet | 0 | nothing; waits | **move to Track details** (information, never an action, and the title is still readable) |
| "Added by URL" | | | 97 | | already dropped, #225 |

Today no row has two flags, so one slot is enough. If a row ever has two, the column shows the
first and `+1`, with the rest in its tooltip (the order is the table's order above, red ones first).

## Layout

Fixed grid, no chip inline with the title:

`chevron | title (1fr) | notes 132px | key 112px | confidence 100px | BPM 84px | genre minmax(110px, 240px) | state 190px`

- **The notes column holds every flag** (tempo flag, Auto warp, vocals near-silent, not renamed,
  stale key tags). It sits right after the title, in its own track, so each flag starts at the same
  x on every row whatever the title length. Flag text that is too long ends in an ellipsis with the
  full words in the tooltip.
- **The title is the only elastic column and ellipsizes.** The full title goes in `title=` so a long
  one is readable on hover. At 1280 px the title gets about 310 px of text (`after-1280.png`); the
  longest titles (97 characters, 19 rows over 50) are cut, which they already were at 490 px. Genre
  gives up its width first, before the title does.
- **The notes column exists only in a view where some row shows a flag** (decided from the whole
  list, not the visible rows, so scrolling never changes it). Views with no flags (a filtered list
  of parked tracks) keep their old title width. This is how the In staging view already adds its
  eighth column.
- **Off its tempo keeps its own grid** (`.row.off`) and is unchanged.

### Wide screens

- **From 1600 px two columns appear** between genre and state: **Familiarity** (Mainstream, Niche,
  Obscure; the value the filter pills count, on 708 rows) and **Tempo from** (`warp`, `reading`,
  `estimate`; today only a `~` and a tooltip say it). Both are already on the row's data, no new
  query.
- **The table cap goes from 92rem to 120rem (1920 px)**, still left-aligned like the rest of the
  page. Past 1920 px the title column simply shows long titles in full (`after-2560.png`, the
  97-character title fits); more width would only stretch the gap between related numbers, which is
  what `--table-tracks`'s comment says the cap is for (#20). The header chips and filter bar keep
  their own widths.
- 1600 px is chosen so a 1440 px laptop keeps the 1280 layout.

## Cost

Small: the row grid (CSS, about 25 lines), moving the flags into one `<span class="notes">` (about
30 lines of markup), moving two things to Track details (the button, the `name pending` flag, about
20 lines), two wide columns (about 20 lines), `--table-tracks`. No store or API change. Tests: a
Playwright pass at 1280 and 2560 px on the Unfinished and Tempo drifts views, asserting every
`.notes .flag` on screen has the same left edge and that no chip is inside `.song`.

## Done when

- **agent:** at 1280 and 2560 px, on rows with short and long titles, every flag's left edge is the
  same; no flag or button is inside `.song`; the notes column is absent in a view with no flags;
  the two wide columns show at 2560 and not at 1280; the near-silent row's button is on its Track
  details.
- **Bamo:** the Tracks page reads better on his screen (check on: Kreayshawn - Gucci Gucci, Queen -
  Bohemian Rhapsody, Stevie Wonder - Superstition, Angel Mina - Destabilize The Mainframe).

## Pictures

- Before at 1280 px, Unfinished view: `30-tracks-row/before-unfinished-1280.png`
- Before at 2560 px, Tempo drifts view: `30-tracks-row/before-drifts-2560.png`
- After at 1280 and 2560 px, a static mock of twelve real rows (not the app):
  `30-tracks-row/after-1280.png`, `30-tracks-row/after-2560.png`

## For Bamo to confirm

1. **Keep the five action flags and drop `name pending` into details.** Recommended: yes.
2. **Move the "Switch to Instrumental" button off the row.** Recommended: yes, it is on 2 rows and
   breaks the grid. Alternative: keep it in the notes column as a small button (needs 190 px).
3. **Cap 120rem, left-aligned, with Familiarity and Tempo from added from 1600 px.** Recommended:
   yes. Alternative: no cap, which puts the title's end up to 1000 px from the key at 2560 px.
4. **Notes column only in views that have a flag.** Recommended: yes. Alternative: always, which
   costs 132 px of title in every view.
