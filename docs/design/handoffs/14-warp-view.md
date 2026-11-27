# Handoff 14 · each stem's warp in Track details

Brief: `docs/design/briefs/14-warp-view.md` · Issue #97 · canvas `Crateside Warp View.dc.html` (1a–1f).
Built against `d6d52c5`. Waveforms, markers and dates in the canvas are made up; *Rapper's Delight*'s
range is the one DESIGN.md records.

## Where it sits

A **Warp** section in Track details' main column, directly under the sample list and above *Why {key}*.
It is open by default and has no fold. The section draws only when at least one sample exists; a
sample with no sidecar still gets a lane (see *None yet*).

**One time axis for the whole group**, not one view per stem. The group is the samples of the
current cut, in the sample list's order; a warp guide still in staging is left out (it is
used up). Clicking a sample line in the list selects its lane.

Section anatomy, top to bottom, in a two-column grid: **200px gutter · 12px · the plot (rest of
the column, 832px at the 92rem cap)**:

1. **Head line**: `WARP` label (the `Samples` label style), then the group line (12px Sans):
   - all stems on the shared warp: `{n} stems · one warp · {markers} markers · {mode}` at `--text-60`
     (`2 stems · one warp · 2 markers · Complex`);
   - any stem with its own markers: `{n} stems · {stem} warped on its own` at `--text`, plural
     `{a}, {b} warped on their own`. This is the only place the group draws attention; it is not colored.
   - right: zoom (below).
2. **Overview strip** (zoomed only, 20px): the first stem's overview at whole-track scale,
   `--text-30`, with the view boxed (1px `--text-60`, fill 8%). Gutter: `Whole track · click to move`.
   Click moves the view's center there.
3. **Ruler** (30px, gutter `Bar`): the loop bar along the top, bar ticks and numbers along the bottom
   (mono 10.5, `--text-45`), and the clip-start notch (a 5px `--text` triangle) at bar 1.
   - Loop: 5px bar from loop start to end, 1px `--text-50` border, filled `--text-55` when on, empty when
     off; `loop` / `loop off` after it in mono 10. Title `Loop 0:14.20–0:34.63 · off`.
   - Clip start title: `Clip start · 0:14.20 · bar 1.1`.
   - Bars are counted from the **shared warp's** bar 1 (the clip start). No labels before bar 1.
4. **Tempo line** (40px, gutter `Tempo from the markers` then the figure and the short note). The tempo
   between each pair of the shared warp's markers, drawn as a step line (1.5px `--text-75`), with a dashed
   `--text-35` line at the figure shown, as on the tempo strip. A two-marker warp is one flat line through
   the dashed one. A drifting one is held between its rounded range, labeled at the right end (mono 10.5,
   `--text-40`); a steady one is drawn ±1 BPM around its figure, with no labels. Title
   `{markers} markers · {tempo note}`.
5. **One lane per stem** (64px plot; gutter: short name 13px/500, provenance chip, a sub line 11.5px
   `--text-50`). The whole lane, gutter included, is the click target; the selected lane takes the house
   chosen-row style (`rgba(255,255,255,.08)`, 1px inset `--text-35` ring, name at 600); others sit on
   `rgba(255,255,255,.02)`.
6. **Time axis** (22px, mono 10.5 `--text-40`): every 30 s whole, 10 s at 32 bars, 2 s at 8 bars.
7. **The selected lane's detail** (below).

## One stem's lane

Inside the 64px plot:

| part | drawing |
|---|---|
| Flag row (0–10px) | markers' flags: a 7×9 `--text` pennant (notched right edge) |
| Overview (11–56px) | Live's min/max per bin, `--text` at 42%. Stereo: two 21px channels, L above R, 2px apart. Mono: one 44px channel |
| Marker lines | 1px from the flag row through the overview |
| Transient row (58–63px) | 5px ticks, `--text-30` |
| Outside the clip | `--bg` at 62% over the lane, left of clip start and right of clip end |

- **Flags or ticks.** Flags are drawn wherever every pair of visible markers is ≥ 9px apart; their lines
  are `--text-45`. Closer than that (a dense warp at whole-track), the flags become 9px ticks in the flag
  row at `--text-70` and the lines through the lane drop to `--text-14`. A dense warp then reads as a
  texture whose bunching shows where the band moved (1b).
- **Transients** draw only when zoomed (32 bars or 8 bars), where they sit ≥ 3px apart. Never at whole-track.
- **A flag's title**: `Marker 3 of 188 · 2:14.06 · bar 63.1 · 111.42 to the next` (the last omits the
  tempo). The tempo goes through `TempoFormat` as a vouched figure.
- **A stem warped on its own** gets a 1px dashed `--text-40` box (radius 4) over its lane from the
  point it parts from the shared warp to the end, titled `Differs from {sibling} from 2:10`, and
  `differs from 2:10` in its gutter sub line. "Parts" = the first marker that is not in the shared
  warp (time and beat within 1 ms / 0.001 beat).
- **No sidecar** (None yet, Waiting for Live): no overview exists, so the plot is a 64px dashed box with
  one centered line (see states).

## Zoom

Three fixed steps as `.pill`s, right of the head line: **Whole track · 32 bars · 8 bars** (the chosen
one `.pill.on.plain`). Bars come from the shared warp. Zoomed, `‹` `›` (`.outline`, mono) move the view
by half its width, preceded by `bars 61–68 · 2:10–2:27` (mono 11.5, `--text-50`). Ruler: whole =
ticks every 4 bars, numbers every 16; 32 bars = every bar, numbers every 4; 8 bars = every bar
numbered with beat ticks between. The zoom is per page visit; it resets on reload.

## The selected lane's detail

A `--surface` box (1px `--line-strong`, radius 8, 18px 20px) under the time axis. Selected on load:
the first stem warped on its own, else the first borrowed one, else the first lane.

1. **Head**: the file name (`Rapper's Delight (Vocal - Full).flac`, 14/600) and the provenance chip.
2. **Facts**, seven columns: label (the stack label style), value (mono 13), note (11.5 `--text-50`):

| label | value | note |
|---|---|---|
| Tempo | TempoFormat figure, its weight | the short note (`from your warp`, `drifts 110–113 · 2%`) |
| Mode | as Live names it: `Complex`, `Complex Pro`, `Beats`, `Tones`, `Texture`, `Re-Pitch` | |
| Markers | count | `shared by the group` · `same as {sibling}` · `its own` |
| Transients | count | `Live's` · `crateside's` · `from the earlier cut` |
| Clip | `0:14.20–4:11.66` | `starts at bar 1` |
| Loop | `0:14.20–0:34.63` | `on` · `off` |
| Default clip | `saved` · `not saved` | `Live uses it` · `Live ignores it` |

   With no sidecar every value is `—` and notes are empty.
3. **Provenance line** (13px `--text-75`, max 760px): the state's sentence (table below).
4. **Controls**, under a stronger rule (`rgba(255,255,255,.16)`): two columns, **Re-apply crateside's
   warp** · **Let Live analyze**, in the side stack's status + action shape: a label line (label left,
   `.outline` button right), then the status as one sentence. Both are always present. Where one does
   not apply, the button is left out and the status says why at `--text-40`. That way the user learns
   the control exists and what would make it apply.

**Transient origin is a fact, not a drawing.** Live and crateside agree 87% within 25 ms, so a second
tick style would show a difference that mostly isn't there.

## Whose warp it is (1d)

Chips are monochrome, following the evidence badges: **filled** for the user's, **outlined** for
borrowed, **dashed** for crateside's starter or Live's unsaved guess. Color appears only for state:
amber for waiting on the user, teal while crateside writes. Chip: 11px/500, padding 2px 8px, radius 999.

| state | chip | gutter sub | provenance line |
|---|---|---|---|
| Your warp | filled `Your warp` | `saved 29 Sep · lent to Vocal - Full` (the lent part only if a sibling borrows it) | `Saved in Live on 29 Sep. crateside lends it to Vocal - Full.` |
| Borrowed | outline `Borrowed from Instrumental - Clean` | `unchanged since 29 Sep` | `crateside copied Instrumental - Clean's warp on 29 Sep. Unchanged since.` |
| Borrowed, since changed | filled `Your warp` | `saved over crateside's · 30 Sep` (+ ` · differs from 2:10`) | `crateside lent it Instrumental - Clean's warp on 21 Sep; you saved over it in Live on 30 Sep, so it is yours now.` + if it differs: ` It follows Instrumental - Clean up to 2:10, then has markers of its own.` |
| Starter | dashed `Starter` | `one marker · not finished` | `One marker at crateside's reading, 94, so dropping it into an empty set sets the set's tempo. Not a finished warp: warp it in Live and press Save Default Clip.` |
| Live's analysis, not warped | dashed `Live's analysis` | `not saved, so Live ignores it` | `Live has opened it, but no warp is saved. Live ignores these markers until you press Save Default Clip.` |
| None yet | dashed, `--text-50`, `No warp yet` | `Live has not opened it` | `Live has not opened this file, so there is no warp and no overview yet.` Plot: `No overview · Live has not opened this file`, `--text-40`, dashed `--line`-ish 14% |
| Waiting for Live | amber filled `Waiting for Live` | `preview it in Live` | `Its warp file is in the Recycle Bin. Preview it in Live's browser and Live writes a fresh one; crateside then puts the warp back.` Plot: `No warp · waiting for Live to open it`, amber text, amber 55% dashed border |
| (transient) Writing | teal filled `Writing` | `writing Instrumental - Clean's warp` | as before the action |
| (transient) Putting it back | teal filled `Putting the warp back` | `Live opened it` | as before the action |

The starter's figure is its TempoFormat reading. *Borrowed, since changed* is drawn as the user's
warp, because it is now theirs; its history is carried by the sub line and the sentence.

## The two controls (1e)

**Availability**
- **Re-apply** is offered where a sibling has a saved warp **and** this stem's differs from it: in
  practice *Borrowed, since changed*, and *Live's analysis* when a sibling has one. Otherwise the
  status, at `--text-40`, is one of:
  - Your warp, lent: `Nothing to re-apply: this is the warp crateside lends.`
  - Your warp, not lent: `Nothing to re-apply: crateside has lent this stem nothing.`
  - Borrowed: `Nothing to re-apply: it is still Instrumental - Clean's warp, unchanged.`
  - Starter / Live's analysis with no sibling warp: `Nothing to lend yet: no stem of this upload has a saved warp.`
  - None yet: `Nothing to re-apply.`
  - During either action: `Not while a change is under way.`
- **Let Live analyze** is offered wherever a sidecar exists. None yet: `No warp file yet: Live has not opened this stem.`

**Re-apply crateside's warp**
1. Offered: button `Re-apply`; status `Writes Instrumental - Clean's warp over yours again. Yours is
   backed up first.` (over Live's analysis: `…over Live's analysis again…`).
2. Confirm (opens below, replacing the status; the label turns amber; the other control's button
   hides): `Replace your warp on Vocal - Full with Instrumental - Clean's? Yours (38 markers, saved 30 Sep)
   is backed up first.` Buttons `.danger` **Replace my warp** · `.outline` **Cancel**.
3. Writing: chip `Writing` (teal); a teal dot + `Writing Instrumental - Clean's warp…`; status `Your
   warp is backed up.`
4. Done: state becomes *Borrowed*; the lane redraws on the shared markers; status at `--text` `Re-applied
   just now. Your previous warp is backed up.` (the time ages as relative time until the page is left).

**Let Live analyze**
1. Offered: button `Let Live analyze`; status `Sends this stem's warp file to the Recycle Bin, so Live
   analyzes it afresh with its own transients. crateside then puts the warp back.`
2. Confirm: `Send Vocal - Full's warp file to the Recycle Bin? Until Live opens the stem again, it has no
   warp.` `.danger` **Send to Recycle Bin** · **Cancel**.
3. Waiting for Live: chip amber; lane is the dashed amber box; label amber; status at `--text`
   `Preview Vocal - Full in Live's browser (Sample Staging). Live writes a fresh file, and crateside puts
   the warp back within a minute.` No button: there is nothing to press here, the next step is in Live.
   This persists across reloads (it is a real file state), and the sample list's *Where it is* drops
   `· warped`.
4. Putting it back: chip teal `Putting the warp back`; teal dot + `Live wrote a fresh file · putting the
   warp back…`.
5. Done: the state it had before (Borrowed, Your warp…); transients note `Live's`; status `Warp put
   back just now, into Live's fresh file. Transients are Live's.`

If the stem is a Starter, step 5 puts the starter marker back. Neither action touches siblings.

## Sample list change

*Where it is* reads `Sample Staging · warped` for any stem with a warp. The `from {sibling}` part
moves to the lane's chip, and a stem waiting for Live reads plain `Sample Staging`. Clicking a sample
line selects its lane (no scroll, unless the lane is off-screen).

## Leave room for (not designed)

- **Editing.** Flags are the handles: they drag along their lane, a click in the flag row adds one, the
  clip-start notch drags. Group edits apply to every stem on the shared warp. 8 bars is the edit zoom.
- **#96, off the grid.** A lane of its own, `Off the grid`, between the tempo line and the stems: ms
  offsets per 10 s window either side of a center line, on the same axis. Offers to correct join the
  detail's controls row as a third column.
- The band's tempo strip and the tempo line say related things; if the warp view proves itself, the
  strip could go. Not in this round.

## DESIGN.md changes

- **Track details** (new subsection *Warp*): the section above, in short. One axis for the group,
  lanes, tempo line, flags-or-ticks at 9px, transients only zoomed, three zoom steps, the detail with
  seven facts, the two controls.
- **Color means state**: add the provenance chip set as the second monochrome example after the
  evidence badges: filled your warp · outlined borrowed · dashed starter / Live's analysis / none.
- **Sample list**: *Where it is* is `Sample Staging · warped`; the source sibling is said by the warp lane.
- **Tempo**: a marker's title tempo and the tempo line's labels go through `TempoFormat`.

## Questions for the build

- "Differs" needs a tolerance: the canvas assumes a marker is shared when time and beat match within
  1 ms / 0.001 beat. Report what real sibling warps look like.
- The overview strip uses the first stem's overview; if a stem is mono and another stereo, take the first
  stereo one.
