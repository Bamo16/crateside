# crateside: judge a track from the bars the maintainer names

Issue: #116

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against the commit that adds this brief. The rules as built are in `docs/design/DESIGN.md`
(Warp view, Side stack, Settings, Colour means state, State chips), and your canvases are in
`docs/design/canvases/`; *Crateside Track Page* is Track details and the warp view's own brief is
`14-warp-view.md`. The terms (window, direction, ear, section) are in `CONTEXT.md` under *Keys*; ADR
0055 and its addenda are the decision. Nothing below is built yet.

---

## Where it stands

crateside judges a track's key from detector readings over the whole file. When the detectors split
three ways or the song is mostly something else (an air horn, a sparse verse, drums), the whole-file
reading is diluted. Houshou Marine's *A Horny Money World* was withheld on exactly that. The
maintainer warped it, said the choruses were **bars 33–48 and 82–96**, and an agent ran the
detectors over those two windows by hand: both agreed on C# minor. It took an ad-hoc session. It
should be a form on the track's page.

A **window** is a stretch of the track detected on its own. Two kinds:

- **The maintainer gives windows**, in bars, with an optional label ("Chorus") and an optional
  note to the judge. The note is the **direction**. They may also say what they hear ("sounds like
  C# minor"), which is their **ear**.
- **The judge asks for windows.** Its verdict can say *withheld, and here are the windows I want
  detected*; crateside runs them unattended and resumes the judge once. The maintainer has not
  asked for those, and should be able to see that they happened and whose they were.

Either way the windows become the verdict's **sections** (a label and its own tonal centre), and
the direction is kept with the run.

## What the maintainer reads bars from

**The warp of the sample Ableton shows.** In Live a sample in the library shows one warp, and the
numbers the maintainer reads off its bar ruler are the numbers they will type. crateside converts
bars to seconds through that warp, so the form's bars match Live's. The warp view (brief 14)
already draws that warp's ruler on Track details, with bars counted from the clip start, and
zoom pills (*Whole track · 32 bars · 8 bars*). With **no warp**, a range is entered in seconds,
and the form says why.

## What the user does

- **Opens "Judge from these bars"** on a track's page. It is offered on any track, and **offered
  first** where the verdict is withheld or the detectors split.
- **Adds one or more ranges**: `from bar` · `to bar`, a label, and a note to the judge. Typing
  `33–48` is the common case. A second range is one more line.
- **Says what they hear**, optionally, as an ear hint: a key, with a mode if they know it.
- **Sees what will happen before it does.** Judging spends Claude quota (about a session's
  start-up, ~37k tokens, per track), so this run is **never in the background**: the maintainer asks
  for it, and it says so. A weekly guard stops background judging at 85% of the limit (ADR 0053);
  this run is the maintainer's choice, so it may be offered while the guard holds, as *Judge this
  one anyway* is today.
- **Watches it run**, then sees the result: the new verdict, each window's detector readings, the
  sections it became, and the direction recorded with the run.
- **Sees windows the judge asked for**, later, in *Why this key*: which ranges, that the judge
  chose them, and what the detectors said over each.

## What the data holds

| | |
|---|---|
| **Window** | start and end in seconds, an optional label, and who asked: the maintainer or the judge |
| **Ranges as typed** | bars, kept as the maintainer wrote them ("33–48") so the form can show them back; the seconds come from the warp at the time |
| **Direction** | free text, with the run |
| **Ear** | the maintainer's key, a source of its own kind. A **setting** decides how much it counts: *hint* (default: weighed, never raises confidence) or *human source* (counted like an authoritative source) |
| **Per-window readings** | each detector's key over that window, as the evidence shows them for the whole file today |
| **A run's state** | detecting windows → judging → recorded. A run that ends without a valid verdict is resumed once before it counts as a strike |

## Questions for the design

- **Where does the form live?** Track details' side stack is one component with a fixed order
  (Confirm · Go · Next · Tags · Name · Uploads · Start over); the warp view is the one place bars are
  drawn. Does the form sit beside the warp view, so ranges can be set by dragging on its ruler as
  well as by typing, or in the stack, or both?
- **Typing or dragging.** Is a range typed (`33–48`), dragged on the ruler, or either? The
  maintainer reads bars off Live, so typing must be fast; a dragged range would show them what
  they are naming.
- **Showing a range on the warp view.** A window is a bar span on the shared time axis. Does it
  appear on the ruler or as a band across the lanes, before, during and after the run? Should the
  judge's windows look different from the maintainer's? (Colour means state, so likely by outline
  and fill, not by hue.)
- **The cost.** How does the form say that this spends quota, once and plainly, without a modal?
  The side stack's pattern for destructive actions is a stronger rule and a `.danger` confirm; this
  is not destructive but it is not free.
- **No warp.** Seconds instead of bars: the same fields, or a different form? The maintainer may
  not have warped a track they want judged, though the usual order is the opposite.
- **A run in flight.** Detecting then judging takes minutes, unattended. What does the page show
  while it runs, and what if the maintainer leaves? The Judging card on the Dashboard exists; the
  track needs its own state.
- **When the judge asked.** In *Why this key*, how is a judge-requested window told from the
  maintainer's? Is it enough to say so in a line, or does the evidence section need a block per
  window?
- **A withheld verdict.** Today it is the weak tone on the confidence and nothing more. Does the
  form's invitation sit in the band under the title, where the verdict is shown?
- **The Ear setting.** One Settings row (the pill pattern: *hint* · *human source*, with a
  one-line gist). Wording only; say if the two values need more than a gist.

## Leave room for

- A range saved as a section the maintainer can reuse on a stem's page, later.
- Windows chosen by a detector rather than a person (find the chorus). Nothing to design now.
