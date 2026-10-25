# crateside: each stem's warp in Track details, drawn as Ableton draws it

Issue: #97

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against `d6d52c5`. The rules as built are in `docs/design/DESIGN.md`, and your canvases are in
`docs/design/canvases/`; *Crateside Track Page* is Track details. Background on the warps
themselves: ADR 0037 (a saved default clip is a finished warp), 0043 (a borrowed warp is finished
and says where it came from), 0047 (a starter warp, and where transients came from) and 0048
(stems arrive in staging already warped).

---

## Where it stands

The user warps a track in Ableton Live: one stem of the group (the drum stem, muted, as a guide) is
warped by ear, and the group shares the warp. Live keeps a stem's warp in a sidecar beside the audio
(`Song (Instrumental - Clean).flac.asd`). crateside now reads and **writes** those sidecars:

- **It lends a warp.** When one stem of an upload has a saved warp, its siblings in Sample Staging
  get the same warp within a minute, so they arrive in Live already warped.
- **It writes starter warps.** A stem of a first cut, with nothing of its upload warped yet, gets
  one marker at the tempo crateside judged, so dragging it into an empty set sets the set's tempo.

Today all the user sees of this is one phrase in the sample list on Track details:
*Sample Staging · warped from Instrumental - Clean*. There is no way to see the warp itself, or to
undo what crateside wrote, without opening Live.

## What the user wants, condensed

- **See each stem's warp the way Ableton shows it**, on Track details, without opening Live.
- **Know whose warp it is**: Live's, the user's own, or crateside's, and if crateside's, borrowed
  from which sibling and whether anything has changed it since.
- **Two ways out** when a warp crateside wrote is not wanted (below).
- **Room to grow.** The user is open to crateside becoming a better warp tool than Live. This
  version only views, but a later one may edit (see *Leave room for*).

## What each stem's warp holds

Everything below is read from the sidecar. Counts are measured over the 1,027 saved warps in the
library and staging (2026-10-02).

| part | what it is | in practice |
|---|---|---|
| **Waveform overview** | Live's own: a minimum and maximum per channel per bin, the finest bin 128 samples (about 345 bins a second), coarser levels by powers of 128. Mono or stereo | a stem runs 2:45 to 5:05 (p10–p90), longest 9:08. Enough resolution to zoom well past a whole-track view |
| **Warp markers** | pairs of (time in the audio, beat in the arrangement) | **most warps are two markers** (885 of 1,027): a steady tempo. The rest run to 47 at p90, 598 at most, bunched where a live band drifts or a step was absorbed |
| **Transients** | where an attack starts, with an energy from 0 to 1 | 450 to 1,400 per stem (p10–p90), about 4 a second, up to 8 |
| **Clip** | start, end, loop start and end, loop on or off | the user cuts the clip to start at bar 1 of the first verse |
| **Warp mode** | Complex or Complex Pro on every saved warp; Beats, Tones, Texture, Re-Pitch exist | show it as Live names it |
| **Tempo** | read from the markers, already formatted by `TempoFormat` (DESIGN.md, *Tempo*) | the song page's headline is this figure for the warped stem |
| **Saved or not** | whether *Save Default Clip* was pressed. Without it, Live ignores the warp | |

A stem may also have **no sidecar**: Live has never opened it, or the user just chose *Let Live
analyze* and Live has not opened it since.

## Whose warp it is

| state | meaning | example |
|---|---|---|
| **Your warp** | the user saved it in Live, or saved over one crateside wrote | most of the library |
| **Borrowed from *sibling*** | crateside copied a sibling's saved warp, and the file is unchanged since | *50 Cent – Many Men (Vocal - Full)*, from its instrumental |
| **Borrowed, since changed** | crateside wrote it, then Live or the user saved over it; it is now the user's | |
| **Starter** | crateside's single marker at its judged tempo. Not a finished warp | a first cut that has just landed in staging |
| **Live's analysis, not warped** | Live opened the file but no warp was saved | |
| **None yet** | no sidecar | |

The **transients** have an origin of their own: Live's, crateside's detector, or copied from an
earlier cut of the same upload. Live and crateside agree on 87% of Live's transients within 25 ms,
so they look alike on screen. Whether the origin is worth showing at all is yours to judge; it
matters only when the user wonders why a snap point is where it is.

## The two controls

- **Re-apply crateside's warp.** Writes the sibling's warp again, after the user or Live changed
  it. It replaces a saved warp, so it needs a confirm step (DESIGN.md, *Side stack*: destructive is
  set apart by position, `.danger` only at the confirm). The old sidecar is backed up.
- **Let Live analyze.** Moves the sidecar to the Recycle Bin. Live writes a fresh one the next time
  the stem is previewed or loaded, with its own transients. crateside then puts the warp back into
  Live's fresh file, keeping Live's transients. In between, the stem has **no warp**: the state
  needs to say *waiting for Live to open it*, and what the user should do (preview it in Live's
  browser).

Neither applies to every state: Re-apply only where crateside has a warp to lend, Let Live analyze
only where a sidecar exists. Show when each is not available, or leave it out; your call.

## What to design

1. **Where it sits on Track details.** The stems of one cut share a length (413 of 414 uploads
   measured) and normally share a warp, so one time axis for the group may say more than a view
   per stem: the user would see the stems agree, or a vocal warped on its own. A stem warped
   differently from its siblings is rare and worth noticing. Sample list, evidence panel and the
   side stack are already on the page; decide whether this joins the sample list, sits beside it,
   or opens from it.
2. **One stem's warp**: the overview, markers, transients, clip and loop, mode and tempo, as a
   reader of Live's clip view would recognize it. Live draws markers as yellow flags above the
   waveform and transients as small gray ticks, and the loop as a bar along the top; use
   crateside's palette (*Color means state*) rather than copying Live's colors.
3. **Two markers and 598.** Most warps are a plain line at one tempo; a few are dense. Both read
   at a glance.
4. **Provenance** for each state in the table, with wording, and the two controls with their
   confirm and in-between states.
5. **Zoom**, if you think it earns its place. The data supports bar-level detail.

## Leave room for, without designing

- **Editing a warp**: moving markers, adding one, setting the clip start at bar 1 of the first
  verse.
- **A warp that sits off its tempo** (#96): crateside measures each 10 s window's offset from a
  beat grid in milliseconds, and finds drift and steps (one warp has a 50 ms step at 4:40 that the
  user absorbed with a 2.5-beat stretch). #96 will flag these and offer to correct them from this
  view. The offsets would lie along the same time axis.

## Constraints

- **Existing tokens, chips and shared controls first** (`docs/design/DESIGN.md`). The tempo figure
  and its note come from `TempoFormat`; don't restyle them.
- **A maximized Chrome window on a 1440p or 4K monitor.** Track details' table cap is
  `--table-tracks` (92rem), with the 19rem rail beside.
- **Drawn from data, nothing fetched**: the overview is the sidecar's own, so no audio is decoded to
  draw the page. Playback is out of scope.

## What to send back

A handoff in the usual form (`docs/agents/design.md` describes the round trip), with:

- the view laid out on Track details, for a two-marker warp and a dense one;
- every provenance state, and the two controls through confirm and their in-between states;
- the wording of every label, note and tooltip;
- anything in `DESIGN.md` that changes.
