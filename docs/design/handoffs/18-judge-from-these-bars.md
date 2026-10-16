# Handoff 18: judge a track from the bars the maintainer names

Brief 18, issue #116, ADR 0055. Canvas: `Crateside Judge From These Bars.dc.html` (1a–1d; 1b and
1d are interactive).

**The rule:** the form lives in the warp view, because bars are drawn there. Ranges are typed or
dragged, and either way they're drawn as bands across the lanes. The band under the title invites
it only when the verdict is withheld or the detectors split. The cost is one status line, with no
modal and no confirm. The run is the track's own state until it is recorded.

## 1a The band

- **Withheld or split**: the Confidence cell keeps its figure (`WITHHELD` at `--text-50`, or
  `LOW`), adds the reason (`The detectors split three ways over the whole file. Name the bars where
  the key is clear.`) and one `.outline` button **Judge from these bars ↓**, which scrolls to the
  warp view and opens the form.
- **Running**: the teal running chip `● judging from bars`, then
  `bars 33–48, 82–96 · detecting window 2 of 2 · started 2:14 PM`. It comes from the server, so it
  survives leaving the page.
- **Recorded**: the new figure, sub-line `judged from bars 33–48, 82–96 · just now`.
- On a confident track there's no invitation in the band. The side stack is untouched.

## 1b Warp view

**Opener**: `Judge from these bars` (`.outline`, small) in the head line after the gist, on any
track with samples. It's hidden while the form is open or a run is going.

**Form**, in a `--surface` box with a 1px `--text` 12% border and radius 8, between the head line
and the ruler:
- Label `Judge from these bars` in amber (open editor), then the unit note at `--text-50`:
  `Bars as Live counts them, from the clip start of your warp.`
- One line per range: number · **range** (mono 13/500, 120px) · **label** (160px) · **note to the
  judge** (the rest) · ×. The range field takes `33–48`, `33-48` or `33 48`. Tab moves through the
  fields, and Enter in the last field adds a line. Then a dashed `+ another range · or drag on the
  ruler below`.
- **What you hear**: key field (mono, `C♯ min`) · optional mode select · hint
  `optional · weighed as a hint, never raises confidence (Settings)`, which follows the Ear setting.
- **Cost line**, left of the buttons, 13px `--text-75`: `Runs now, while you watch: the detectors
  over {n} windows, then one judge session of about 37k tokens, {p}% of this week's Claude quota.`
  When the 85% guard holds, add `Background judging is paused at 85% of the week; this one is
  yours, so it runs anyway.`
- Buttons: **Judge from these bars** (`.primary`) · Cancel. No confirm step.

**Dragging**: the ruler takes a crosshair while the form is open, and its gutter says `drag here to
add a range` in amber. A drag snaps to whole bars and adds a filled line. A band's edges drag to
edit it, and the list and the bands are one model.

**Bands** (one per window, from the ruler down through the tempo row and every lane):
- Yours: solid 1px `--text` 50% border, fill `--text` 6%. On the ruler, a tag at 14% fill: `1 ·
  33–48` while editing, `Chorus · C♯ min` once recorded.
- The judge's: dashed (4 3) at 50%, no fill, tag `judge · 57–72` on `--bg`.
- The window being read: teal border and tag, teal 10% fill. A window already read goes back to
  neutral (`1 · read`).

**Running**: the form folds into a teal-bordered line with step chips (the stage-flow chips:
● Detecting windows → ○ Judging → ○ Recorded), then `Detecting window 2 of 2 · bars 82–96` in teal,
and on the right `You can leave. It carries on, and the Dashboard's Judging card shows it.`
A run that ends without a verdict is resumed once, and the line says `Resuming once: the judge
ended without a verdict.`

**Recorded**: a neutral line `Recorded just now: C♯ minor, HIGH. Both chorus windows agreed, with
your note and your ear kept with the run.` and `Why this key ↓`. Withheld again: the same line
says so and the form reopens with its ranges kept.

**Judge asked**: its band is dashed, and the line reads `The judge asked for bars 57–72 (16 Oct,
2:31 PM) and crateside ran them unattended.`

**No warp**: the same form. Ranges are in `m:ss–m:ss`, the column head is `From – to`, the unit
note reads `No warp yet, so ranges are in minutes and seconds. Warp it in Live and the form takes
bars.`, and the ruler counts time. Dragging works on it the same way.

## 1c Why this key

- First, **Your note to the judge** (quoted, 13.5px `--text-85`) and a line `Your ear: C♯ min ·
  weighed as a hint · run of 16 Oct, 2:14 PM`.
- **Windows**: one block per window, radius 8. On the left (250px): a who-badge (`yours` filled
  like an authoritative badge, `judge` dashed), the label, the span in mono (`bars 33–48 ·
  0:45–1:07`) and a why line. A judge's block quotes its reason and says it was run unattended.
  On the right: the detector table in the whole-file panel's shape, then `→ section Chorus: C♯
  minor` (or `→ no section: …`). Yours have a solid border at 16% and a faint fill; the judge's are
  dashed at 22% with no fill.
- The whole-file reading folds under them: `▸ The whole file · split three ways: E maj, C♯ min,
  A maj`.

## 1d Settings: Your ear

A row in Keys (or with the judging rows): name **Your ear**, gist `How much the key you say you
hear counts.`, pills **Hint** · **Human source**. Status:
- Hint: `Weighed with the rest, but it never raises confidence on its own.`
- Human source: `Counted like an authoritative source: it can settle a split by itself.`

## Leave room for

Saving a range as a reusable section on a stem's page. Detector-chosen windows would be a third
band style (dotted), not designed.
