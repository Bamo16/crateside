# Design brief — re-cut stems, and renaming

Two new capabilities for the **crateside** Tracks page. The app exists, the page is built, and
its visual language is settled — **everything you need is in this document; there is no repo to
read.**

You designed this page already. If the folder `design_handoff_unified_tracks_page` is to hand,
its `README.md` and `Crateside Unified Page.dc.html` are that earlier pass and are still the
reference — but read **"What changed since that spec"** below first, because the built page has
deliberately diverged from it in six places.

---

## What crateside is

A five-stage pipeline — evidence → verdict → separation → handoff → tagging — that turns a
YouTube playlist into key-tagged stems in the user's Ableton library, with no human step until
warping. One page, `Tracks`: a frozen header showing what is running, filter pills, and one row
per track that never moves, because state is a column rather than an address.

**Vocabulary matters here and the user will notice if it slips:**

- A **sample** is a file meant to be kept, tagged and warped. A **stem** is one origin of a
  sample. An instrumental release's kept file is its *source track* — a sample, and no kind of
  stem at all.
- A **warp guide** is the drums stem. It is *not* a sample: it is used up rather than kept, and
  deleted after warping. It **is** tagged like everything else — never say "never tagged".
- **Sample Staging** is where stems land and wait, for weeks. **Mashup Samples** is the finished
  library. A file's folder is the only signal for whether the user has finished with it —
  crateside never claims a file has been "warped", only where it is.

---

## Design tokens (exact, from the running app)

**Surfaces** — page `#141310`, header/bar `#1b1a15`, raised (toast) `#23211a`,
ink-on-filled-chip `#0f0e0c`.

**Text** — `#ece7dc`, dimmed as alpha over the page background:

| token | alpha | contrast | use |
|---|---|---|---|
| text | 1.0 | 15.1:1 | |
| text-75 | .75 | 8.8:1 | body prose |
| text-60 | .60 | 6.0:1 | secondary values |
| text-50 | .50 | 4.5:1 | floor for anything read as prose |
| text-40 | .40 | 3.3:1 | **labels and column heads only, never a sentence** |
| text-30 | .30 | 2.4:1 | decorative: a chevron, an em dash for a missing value |

**The four colours, and nothing else is coloured:**
teal `#6fc7d6` = the machine is working · amber `#e8b45a` = the user's turn ·
red `#e07a6d` = needs attention · green `#7fcf9a` = done, and behind them.

**Dividers** — rows `rgba(255,255,255,.06)`, header edges `rgba(255,255,255,.08)`.

**Type** — IBM Plex Sans for text, **IBM Plex Mono for every number, key, timestamp and count**
(they are compared down a column, which proportional digits defeat). Sizes 22/16/15/14/13/12.5/
12/11.5/11/10.5px; weights 400/500/600. Uppercase labels are 10.5–11px, letter-spacing .08em.

**Radii** — pills 999, buttons and stem lines 6, toast 10, flags 4.
**Grid gap** 14px. **Page gutter** 22px. **Row height** 46px (52 in the In-staging filter).

**Buttons** — outline: 12px/500, transparent, 1px `rgba(255,255,255,.18)`, radius 6, padding
6px 11px; hover border `.4`. Primary: `#ece7dc` on ink, no border, padding 7px 12px.

---

## The current page, exactly as built

**Collapsed row** — `grid-template-columns: 24px 1fr 90px 100px 80px 190px`, gap 14, padding
0 22px:

    ›   Song [flag]        Key      Confidence   BPM   State chip

The In-staging filter adds a seventh column (`1fr`) reading `staged 3 weeks ago`, and grows rows
to 52px.

**State chips** — the full vocabulary, and anything new must fit inside it:

| chip | style |
|---|---|
| queued | neutral outline |
| detecting key | teal filled + dot |
| judging | teal filled + dot |
| judged · in line | teal outline |
| separating | teal filled + dot |
| staging | teal filled + dot |
| tagging | teal filled + dot |
| ready to warp | amber filled |
| parked · no key | red outline |
| gave up · {stage} ×3 | red filled |
| held | dashed outline |
| done | green outline |

Teal filled: bg `rgba(111,199,214,.14)`, text/dot `#6fc7d6`, border `rgba(111,199,214,.3)`.
Amber filled: bg `rgba(232,180,90,.16)`, text `#e8b45a`, border `rgba(232,180,90,.35)`.
Red outline: text `#e07a6d`, border `rgba(224,122,109,.45)`; red filled adds bg
`rgba(224,122,109,.18)`. Green outline: text `#7fcf9a`, border `rgba(127,207,154,.3)`.
Neutral: text `.75`, border `rgba(255,255,255,.14)`. Dashed: text `.75`, 1px dashed
`rgba(255,255,255,.3)`. **The dot marks work happening this second** — the filled teal states
only.

**Expanded panel** — `grid-template-columns: 1fr 340px`, gap 32, padding `6px 22px 20px 60px`
(indented past the chevron so it reads as belonging to the row above).

*Left column*, one of two bodies depending on whether separation has happened:

- **Before** — heading `WILL SEPARATE INTO`, then the planned recipe, then an
  `Instrumental source` toggle (34×20, radius 10, on = amber with an ink knob) with the hint
  "changeable until separation starts".
- **After** — heading `STEMS · SAMPLE STAGING / {song}`, then column heads
  `SAMPLE · ABLETON KEY · WHERE IT IS`, then one line per sample.

Stem lines: `grid-template-columns: 1fr 120px 140px 1fr`, gap 14, padding 8px 12px, radius 6,
margin-bottom 4, bg `rgba(255,255,255,.03)`, 1px `rgba(255,255,255,.12)`. The **warp guide** line
is the same grid with a transparent background, a **dashed** border and opacity .55, its label
suffixed with mono 11px "warp guide", its tag cell an em dash, its note "for placing markers;
deleted after warping". A **skipped** plan line is transparent, opacity .45, struck through, with
an italic reason.

*Right column* (340px, padding-top 22px): the `Next` sentence, then a detail sentence
(12.5px/1.5, text-75) reading e.g. "Judged E major, high confidence. Staged 18 hours ago.", then
a row of outline buttons: **Open folder · Why this key · Hold · Remove from playlist** (and
**Retry** when a stage gave up, **Release hold** as a primary button when held).

---

## What changed since that spec

Six deliberate divergences from the earlier pass — carry them forward, do not undo them:

1. **The `Next` column is gone.** The chip already says what state a track is in, so a sentence
   restating it was a column of noise. The sentence moved into the expanded panel. **This leaves
   room for one to three new columns**, which is worth knowing for what follows.
2. **The stem lines gained column heads** and a third column reading **where the file is** —
   `Sample Staging` or `Mashup Samples` — replacing a "warped ✓ / not yet" column. crateside
   never watched anyone warp anything; it read a folder.
3. **The warp guide's tag cell is an em dash**, not "never tagged". A guide is tagged like
   everything else and is excluded for being used up.
4. **A "Why this key" button** links each row to a full key-detection evidence page.
5. **Settings is a third nav tab** — Tracks · Partners · Settings.
6. **The separation and judging header cells each have a Pause/Resume button**, and a paused cell
   shows an amber dot and reads "paused" rather than "idle".

---

## Capability 1 — Re-cut stems

The user marks a track as an *instrumental release* when queueing it, which derives a recipe of
drums-only plus keeping the source file. Sometimes he discovers **during warping** that the track
actually has vocals and he needs a vocal/instrumental split instead. **Two tracks are stuck on
this right now.**

He wants to change the requested stems from the GUI and have crateside redo separation, put the
new stems into `Sample Staging`, and remove the now-unwanted source file.

Constraints, all measured against the running code:

- **Key detection must not re-run.** It costs Claude quota — about 5.4% of a five-hour window per
  track — and the verdict is already correct.
- **Restricted to tracks whose files are still in `Sample Staging`.** The user is content to move
  files back from `Mashup Samples` by hand to invoke it.
- **That restriction guarantees the original downloaded audio is still on disk**, so nothing is
  re-downloaded. crateside discards a track's working set only once separation has succeeded
  *and* staging is empty.
- **The recipe is derived from a recorded fact, never chosen directly.** The user states
  *"this release has no vocals"*; the recipe follows. The stated direction is that this toggle
  becomes a **preset picker** — so design the surface so a picker can replace the toggle without
  redrawing everything around it.
- **Re-cutting destroys the track's existing staged files.** The surface has to make that
  consequence legible before it happens.

**Design what re-cut looks like in the "after separation" panel body** — the one headed
`STEMS · SAMPLE STAGING`.

## Capability 2 — Renaming

Track names come out as `Artist - Title`, derived from the ledger, then the staging board, then
the playlist title. The user frequently wants to correct them and renames files by hand today,
which is error-prone. Real examples:

- an artist field that is a duplicated eleven-name pile-up, which should read `Jamie Paige`
- `星街すいせい`, which he wants romanised to `Hoshimachi Suisei` because he cannot read Japanese
- titles containing `(OFFICIAL AUDIO)`

He wants to rename from the Tracks page. The new name must be what the GUI shows **and** what the
files on disk are called — hence what Ableton shows.

Constraints:

- **Allowed any time before the files reach `Mashup Samples`.** So it may happen before anything
  has been downloaded, while separation is running, or with files sitting in staging.
- Each stem file is named `<track name> (<preset>)`, so **one rename touches several files** plus
  an `Ableton Folder Info` XMP entry.
- Provenance lives in each file's `COMMENT` tag, not its filename, so **renaming files is safe**
  for every join crateside makes.
- The separator names stems from the **source audio's tags**, so a rename made before separation
  has to reach those.
- The name is derived from three sources in priority order, so **a rename is an override** that
  must win over all three and must be **visibly** an override.

---

## Artboards wanted

1. The **re-cut affordance** in the expanded row, including its confirmation state.
2. The **rename affordance**, including how an overridden name is marked as overridden, and how
   it is reverted.
3. How a **rename or re-cut in progress** reads in the collapsed row — reusing the chip
   vocabulary and the colour rule, or arguing for an addition to it.
4. A **notes artboard** listing the open questions the drawing cannot settle.

Two are known already and can seed that last artboard: whether a re-cut is a *new fact* about the
release or a *correction* of the old one, and whether a rename override survives a later
re-judging that records a better artist and title.
