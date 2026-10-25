# crateside: Counts becomes the dashboard

Issue: #78

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against `9077ac3`. The rules as built are in `docs/design/DESIGN.md`. The page today is
`src/Crateside/Components/Pages/Counts.razor` (built from handoff 08). The Tracks page header
(`Tracks.razor`, the three header cells) and the activity log (`Components/ActivityLog.razor`) hold
most of the live status mentioned below. Your canvases are in `docs/design/canvases/`.

---

## What changed

Brief 08 said this page was "a fun thing to look at … not a dashboard". **The user has reversed
that (2026-09-28):** it was always meant to be the dashboard, the main page you look at to *see
what's going on*. It keeps everything it shows today and gains more.

The user's direction, in short:

- Design it **as you would for a customer if this were your product**: practical and modern,
  following current dashboard practice, not decoration.
- **Maybe customizable.** He is unsure. Give a recommendation (see *Open questions*).
- The normal viewport is a **maximized Chrome window on a 1440p or 4K monitor**. Use the width.
  It must still hold up narrower, but wide is the norm, not the exception.

## The detector panel was a misunderstanding

When the user asked for detector analytics, he meant the **key** detectors. The BPM panel
(*Detectors against the warps*) is fine and stays, but a **key-detector panel** is what he wanted.
The figures are the ones he put together for Fil, who maintains ffmpeg's `keydetect`.

crateside runs three key detectors on every track: **keydetect** (Fil's ffmpeg filter), **crema**
and **librosa**. It then hands their results, keydetect's chord list, and web sources to a Claude
agent, which gives the verdict and a confidence. Measured 2026-09-28 on the keytag ledger, latest
run per song:

| verdicts counted | songs | keydetect | crema | librosa | all three wrong |
|---|---|---|---|---|---|
| HIGH or MEDIUM | 397 | **73 %** (290) | 65 % (259) | 59 % (234) | 30 |
| HIGH only | 192 | 78 % | 71 % | 62 % | 13 |

**How the misses relate to the verdict** (HIGH or MEDIUM) is a finding in its own right. A fifth
or a relative key is harmonically close, so for a mashup it is a near miss, not a wrong pairing:

| miss is the verdict's | keydetect | crema | librosa |
|---|---|---|---|
| fifth (up or down) | 35 | 44 | 47 |
| relative major/minor | 26 | 18 | 22 |
| parallel major/minor | 22 | 13 | 31 |
| anything else | 24 | 53 | 63 |
| no reading | 0 | 10 | 0 |

The **30 songs where all three detectors are wrong** are where the Claude verdict earns its place.
They are also test cases the user wants to add to Fil's corpus, so a way to see that list (and copy
it, as the BPM panel's bands already allow) is useful.

## What the page holds today

Under a sticky index: **By key**, **By tempo** (with the shape line *Steady · Mostly steady ·
Drifting*, and under it *beatdetect flags as drifting*, one count per shape), **By genre**, *What
the sources called the rest*, **By familiarity**, **By artist**, **Detectors against the warps**
(BPM: keytag and beatdetect in bands, with a strip of where the misses land), **Files with no key**.
All of it stays, in some form.

## What "what's going on" can draw on

All of this exists in crateside now. What to show, and how prominently, is yours to propose. Not
all of it deserves a place.

**Live, right now**
- **Separation** (GPU): the track being separated, time elapsed, paused or not, how many wait.
  About 5–7 minutes per track.
- **Judging** (Claude): the track being judged, elapsed, paused or not, how many wait.
- **Claude quota**: percent of the 5-hour window used, and when it resets.
- **YouTube quota**: units used today out of 10,000 (Settings shows it).
- **Library backfill**: on or off, waiting / judged / gave up, and when the next one goes.
- **Background readers**: beatdetect reading the instrumentals (for example *116 of 393 read*),
  plus genre and familiarity lookups. Unattended, CPU or network only.

**Waiting on the user.** Amber in DESIGN.md means *your turn*, so this is where amber belongs.
- The Tracks filters, with counts: *In staging* (ready to warp), *Needs attention* (stopped,
  parked, or carrying a finding), *Tags to answer* (genre or familiarity).
- Tracks that gave up at a stage, and failed playlist removals (*Left in the playlist*).
- New from #75: a **tempo drifts** flag on tracks not yet warped (expect a hard warp).

**Over time.** `jobs.jsonl` holds every stage since 2026-08-16 (about 5,100 lines).
- Tracks finished (tagged) per ISO week: 23, 23, 101, 199, 142, 111, and 12 so far this week.
- About 300 separations, 300 verdicts and 600 handoffs, with failures and cancellations for each.
- The activity log: recent stage events and deletions.

**The library**: everything on the page today, plus the key-detector panel above.

## Open questions, for you to propose and the user to confirm

1. **Customizable or not.** My recommendation, as the implementing side: no free-form widget
   grid. There is one user, the layout would need storing, and a well-ordered fixed page serves
   better. At most, collapsible sections whose state is remembered. Argue the other way if you see
   a better case.
2. **Is it the landing page**, and so the first tab? The tab row today is Tracks · Counts · Tools ·
   Settings. The user's words suggest yes.
3. **Its name.** *Dashboard* is the user's word.
4. **How live it is.** The Tracks header already updates as stages move. Should the dashboard
   update the same way, or on a re-read?
5. **Where each tile leads.** A count of *Needs attention* is most useful as a link to that Tracks
   filter. Say which tiles are links and where they go.

## Constraints

- No new tokens, and nothing newly colored (`DESIGN.md`). Teal = the machine is working, amber =
  your turn, as on the Tracks page.
- The bar-table style (key, genre, tempo) is the house style for counts. Reuse it where it fits.
- Buttons are the existing `.outline` / `.primary` / `.danger`. Pills exist for choices.
- Counts are one per song, not per file.
- Whole-number BPMs everywhere except Re-Pitch.

## What to send back

A zip holding the handoff (`11-dashboard.md`), the canvas, `support.js` only if it changed, and a
README saying where each goes. The link route (`docs/agents/design.md`, handoff 06c) is preferred.
