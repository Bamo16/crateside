# crateside: how a BPM reads, in every place one appears

Issue: #92

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against `58a2dae`. The rules as built are in `docs/design/DESIGN.md`, and your canvases are in
`docs/design/canvases/`. The decision this brief serves is
`docs/adr/0049-a-bpm-shows-only-the-precision-crateside-can-vouch-for.md`: read it first. It fixes
what each tempo state *means*. This brief asks how each one *reads*.

---

## Where it stands

Every BPM crateside shows is rounded to a whole number: the BPM column on **Tracks**
(`Components/Pages/Tracks.razor`), the BPM in the **song page** header with its grey note beneath
(`Components/Pages/Song.razor`), the evidence panel's keytag and beatdetect figures
(`Components/Evidence.razor`), and on **Counts** the BPM histogram, the detector table and each
artist's BPM range (`Components/Pages/Dashboard.razor`). **Tools** already fills a picked track's
BPM to two decimals (`Components/Pages/Tools.razor`).

Rounding was right when the detectors were rough. They no longer are. Crateside can now tell, before
anyone warps a track, whether its tempo is 128.00 or 128.02, and nearly a third of the user's steady
warps are not whole numbers.

## What the user wants, condensed

- **A BPM read at a glance says what it means about the track.** If `150` sometimes means
  "measured exactly" and sometimes "roughly", it can never be trusted to mean the first. So a bare
  number is reserved for a tempo crateside can vouch for, and everything else visibly is not one.
- **Once the user has warped a track, the warp is the number**, everywhere, charts included. Any
  other BPM shown beside it says what it is.
- **A drifting tempo shows that it drifts**, with soft whole-number figures, not false precision.
- **A track that is steady apart from an intro, outro or interlude** shows its steady tempo, with
  a word that a part changes.
- **A track that really changes tempo** only needs to say so. It is the rarest case and the one he
  cares least about. Show what is reliably known; the bar is low.

## The states (ADR 0049)

Each comes with real tracks from his library, so the canvas can use true figures.

| state | figure | examples |
|---|---|---|
| **Vouched for, whole**: his warp, or a reading crateside trusts to the hundredth | `128` (never `128.00`) | *Flo Rida – Low* (warped at 128.002, shown 128); *1991 – If Only* 174 |
| **Vouched for, with hundredths** | two decimals, never one | *Paul Oakenfold – Ready Steady Go* 128.02; *Eminem – My Name Is* 85.52; *Mr Oizo – Jo* 119.50 |
| **Approximate**: steady, but crateside cannot vouch for the hundredths | `~123` | *Smash Mouth – Walkin On The Sun*, read 123.03, before any warp |
| **Drifting**: no tempo holds (live players, no click) | `~111`, marked as drifting; range where there is room | *The Sugarhill Gang – Rapper's Delight*, 110–112; *Cypress Hill – Hits from the Bong*, 91–94 |
| **Mostly steady**: a main tempo plus a ramp or a drifting stretch | the main tempo as vouched, and a note that a part changes | a track with a free-time intro |
| **Changes tempo**: two or more stable tempos | the main tempo and a note that it changes | *ReeK – Deity Mode*: 180, with 24 bars at 132 |
| **keytag's estimate**: shown only before crateside has read the track | approximate, and labelled as keytag's | a track still being judged |

The tilde is the ADR's working mark for "approximate". You may propose a different one, as long as
"vouched for" and "approximate" can never be confused, and the mark means the same thing everywhere.

**Where a figure came from** has three answers: *your warp*, *crateside's reading* (beatdetect on
the instrumental), and *keytag's estimate*. The headline is always the strongest there is. Today the
song page's grey note says "from your warp" or "keytag's estimate"; that is the place to say it.

## What to design

1. **The Tracks column.** One figure per row in a narrow column, mono today. Whole numbers and
   two-decimal figures sit in the same column: how do they align (by the decimal point, or right
   aligned)? How do approximate, drifting and changing rows read there without widening the column
   much? Rows ready to warp are tinted today; keep that working.
2. **The song page header.** The headline figure, and the note beneath saying where it came from
   and its shape. For drifting: the range. For mostly steady and changing: the note that says so,
   and the main tempo's share if it helps.
3. **Secondary figures with their labels.** The evidence panel shows keytag's figure and
   beatdetect's next to the headline. Each needs a label saying what it is, and they must not read
   as the headline.
4. **Counts.** The BPM histogram and the detector table use headline figures. The artist table
   shows a BPM range (`85–128`). Decide whether ranges and bins show hundredths or stay whole: they
   summarise many tracks, so whole numbers may be honest there.
5. **Tools.** The picked track fills its BPM, and an arrangement BPM can be clicked to copy (the user
   pastes it into After Effects; Ableton itself is typed into). What the field shows, and what a
   copy puts on the clipboard, for an approximate or drifting track.

## Constraints

- **Two decimals or none**, matching Ableton's tempo field. `119.50`, not `119.5`.
- **Every place uses the same marks.** The implementing agent builds one formatter that every page
  calls, and a test stops a page from formatting a BPM itself. So design the states once, as a small
  system, then show each place using it.
- **Existing tokens and chips first** (`docs/design/DESIGN.md`). Add a token only if the states
  cannot be told apart without one.
- **A maximised Chrome window on a 1440p or 4K monitor** is the norm; Tracks is a dense table.

## What to send back

A handoff in the usual form (`docs/agents/design.md` describes the round trip), with:

- the states as a small reference: each one's figure, mark, label and tooltip, if any;
- each place listed above, laid out with real rows from the table of states;
- the wording of every note and label;
- anything in `DESIGN.md` that changes.
