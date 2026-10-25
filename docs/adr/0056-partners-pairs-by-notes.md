# 0056 — Partners pairs by notes

- Status: Accepted
- Date: 2026-10-17
- Follows [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md) (a verdict is a tonal
  center and the tag is its collapse). Built in [#126](https://github.com/Bamo16/crateside/issues/126)
  (core) and [#125](https://github.com/Bamo16/crateside/issues/125) (the page, brief 19).

## Context

Partners was ported from the PowerShell cmdlet `Get-MashupPartner` and tested for parity with it.
[The note of 2026-10-17](../notes/2026-10-17-partners-and-the-modal-verdict.md) measured the ported
code against all 84 tonal centers:

- Its source is the samples' most common Key tag. It never reads the verdict, so a mode never
  reaches it.
- Its shift moves the partner so its tonic lands on the source's tonic, fourth or fifth. The one
  exception is the relative, which gets shift 0 because it has "same notes, other tonic". For a
  modal source the key with the same notes is not the tag's relative. Partners puts *A Dorian*'s
  G major at +2 (5 of 7 notes once moved, against 7 unshifted) and, at the default ±1, does not
  list it.
- Its relationship labels were never designed. `Modulation` is a catch-all, and since ADR 0055 it
  reads like a key change within a song.

How the maintainer pairs, in their words (2026-10-17): by ear, one vocal over one instrumental.
With about 500 tracks they mostly try the same key and the relative (same notes), and move on if
neither works. The wider list matters for an instrumental they are set on using, and Re-Pitch
pairings need the tool because key and BPM move together. Of the labels they read only "same key"
and "relative", and they sort by shift.

The design was settled with the maintainer in a grilling round on 2026-10-17.

## Decision

**Partners works from the verdict.** The source is the latest run's primary tonal center. Only a
track with no verdict falls back to its samples' Key tag. A key chosen by hand is a tonic and a
mode.

**A key holding the source's notes needs no shift.** The shift rule stays tonic-aligned, but its
shift-0 exception widens from "the tag's relative" to every key whose seven notes are the
center's. For plain major and minor this is the relative alone, so nothing changes for them. For
*A Dorian*, G major and E minor are both at shift 0.

**A row says how many notes it shares.** *Notes shared* counts the source center's seven notes
that the partner's scale holds once moved by its shift, shown as `6 of 7`. The rows sort by
absolute shift, then by notes shared. Only two labels remain, **same key** and **same notes**.
The cmdlet's score and its mood names (Energy Rise, Relaxed, Mood Shift, Modulation, "Relative
(Epic)") go. A modal source's own center is its same-key row (*A Dorian*, 7 of 7), for consistency with a
plain key's. Its tag (*A minor*) is an ordinary row.

**The shift range defaults to 0** and can be asymmetric (−1 to +3, say). Its control is the
design's (#125).

**Sections are a choice of basis.** For a verdict with sections, every section's center is
visible at once, with the primary chosen by default. Until sections have times they are shown in
the order the ledger holds them. Partners itself still pairs one center with one key.

**A mixed verdict pairs as its collapse**, with no same-notes exception, since its notes are two
scales.

**The cmdlet is no longer the spec.** `PsMashupParityTests` and its fixture are retired. The
behavior worth keeping is restated in `PartnersTests`.

**Partners still names keys for Ableton's browser.** It does not list tracks, and does not write
tags to the files.

## Considered and not taken

- **Re-basing on the same-notes key** (brief 17's handoff, *1d*). It trades tonic-aligned partners
  for notes-aligned ones (4 of 7 keys in common at shift 0 for *A Dorian*), and the tag basis
  would still give the +2 advice for G major.
- **A scratch tag written to every pairing file**, so Live's browser filters on one tag.
  Ableton's filters can only AND: the maintainer checked on 2026-10-17 that Ctrl-click within a
  group ANDs, that `#Key|Major` is the search bar's group-and-tag syntax, and that `OR` and `|`
  do not alternate. A scratch tag would sidestep that. Not taken: the maintainer has seen tags go
  stale in Live before, and rewriting tags on every pick would multiply the chance.
- **Listing the browser filters to click** (all 7 shift-0 partners of any plain key come down to
  4 clicks), **track counts per key** (the Dashboard already shows them), and **the BPM a
  shifted partner needs**. The maintainer judged the first too particular to their own habit, the
  second as belonging on the Dashboard, and the third as more confusing than helpful in the table.

## Consequences

- The modal fix the maintainer needs most is not here. They know the relative as ±3 semitones,
  which is wrong for a modal track, and that is shown on the track (brief 17, #114).
- Modes go on the files as a third Key tag beside the collapse (#127). Partners does not depend
  on that.
- A section-by-section mashup (one source's chorus over another's verse) has its basis here, but
  the partner side stays a single tag per file.
