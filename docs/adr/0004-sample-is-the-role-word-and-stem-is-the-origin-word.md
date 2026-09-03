# 0004 — "Sample" is the role word, "Stem" is the origin word

- Status: Accepted
- Date: 2026-08-15
- Reverses: the "avoid *sample*" resolution previously recorded under *Flagged
  ambiguities* in this repo's `CONTEXT.md`

## Context

The glossary previously ruled the word *sample* out: *"In DJ usage it means any asset used in
production, which is too broad to be useful. Use **Track** for source material and **Stem** for
a separated part. `Sample Staging` and `Mashup Samples` keep the word only because they are
existing folder names on disk."* The folder names were treated as legacy baggage.

They were not. The user chose that word deliberately, and the breadth is the point: what lands
in those folders is not always a separated part.

**Stem** describes how a file was made. The board needs to know what a file is *for*, and those
two axes cross:

- A **drums** file and an **instrumental** file are equally stems. Only one is kept.
- An already-instrumental release (Snail's House and similar) is separated for drums only. The
  file the user warps, tags and keeps is **the source track**, which is not a stem at all.

So "separated part" cuts across the distinction that matters. The concrete symptom:
`StagingSong.IsFullyTagged` is `TaggedStemCount == Stems.Count`, which counts a drums file that
is never going to be tagged. A warp guide therefore reads as a permanent untagged stem, clearing
only when the file is deleted — indistinguishable on the board from "you have not tagged this
yet", so it cannot say which step is actually outstanding.

## Decision

**`Sample` returns as a first-class term, defined by role.** A sample is a file the user mixes
with: key-tagged, destined for `Mashup Samples`. Usually a stem; sometimes a whole track.
`Stem` keeps its meaning and is demoted to describing origin only.

The "too broad to be useful" objection does not apply here, because the term is defined
operationally on disk: **a sample is a thing that ends up in `Mashup Samples`.** The folder is
the extension of the term. DJ-usage vagueness is a problem in general conversation, not in a
domain where the boundary is a directory the user built.

**`Warp guide` names the drum stem's role, and it is a role rather than a kind.** A stem
produced to be looked at rather than heard, so warp markers can be placed against its
transients, then deleted as a step of the workflow. Modelling it as "not a sample" was
considered and rejected: a drum stem is perfectly mixable, and the user can imagine keeping one.
Calling it a non-sample would make that case a contradiction in the glossary instead of a
deviation from it. **The distinguishing axis is retention, not role.**

**crateside treats every drums stem as a warp guide, with no override.** Keeping a drum stem is
rare enough that the user would do it by hand, outside crateside. This is a deliberate
simplification, recorded so that it reads as one.

**Completeness is asked of samples, not of files.** "Is this song finished?" becomes "is every
sample tagged?". A warp guide's presence is reported as its own state — warping outstanding —
rather than as an untagged file.

**An instrumental release is recorded as `has_vocals: false` in the ledger, stated by the user.**
Not `instrumental`, which already names a *stem preset* — the track minus its vocals — and a
release like this will never have one; writing `instrumental: true` on a track that can never
have an `Instrumental - Full` file is the same overload this ADR exists to undo. It sits at the
top level rather than inside `verdict`, because it is something the user says rather than
something the agent concluded from evidence, and it is kept distinct from `input_kind`, which
says *which file was analysed* — for one of these the analysed file genuinely is the full mix.
It records the **fact** rather than the plan ("separate drums only"), so it still explains itself
when the workflow changes, and crateside derives the plan from it.

**Absent means "not stated", not "it has vocals"** — the same null semantics as `verdict.mode`,
for the same reason: no run recorded before the field existed said anything about vocals, and
none of them may acquire the fact retroactively. What to do about not-stated is the consumer's
policy, and crateside's policy is to expect the usual vocal and instrumental pair.

## Considered and rejected

- **Keep *sample* out and coin a different role word.** Rejected: the folder names already teach
  the term, and a synonym would compete with the disk layout the user reads every day.
- **Define *warp guide* as a kind of file that is not a sample.** Rejected per the retention
  argument above.
- **Give crateside an override for a retained drum stem.** Rejected as speculative: the user
  named the case and immediately said they would work around crateside for it.

## Consequences

- `IsFullyTagged` must stop counting warp guides, and the board needs a way to say "warping
  outstanding" that does not borrow the untagged marker.
- An instrumental-only track has a **sample that is not a stem**. The staging model, which reads
  `StagingStem` per file, has to admit a retained source. StemForge already tags a plain
  download with provenance and a null preset, so the ledger join works; what is missing is a way
  to tell a deliberate keeper from a stray file.
- **A warp guide's deletion is a candidate warp-completion signal** — the thing
  [ADR 0002](0002-there-is-no-warped-signal.md) concluded does not exist, arriving from the
  user's habit rather than from Ableton. Measured at 8 staged songs: the one without drums
  carries an `.asd`, one has both (mid-warp), six have drums and no `.asd`. **Consistent, and
  far too small to build on** — one positive case is a coincidence with a story attached. It is
  recorded here as something to re-measure, not as a finding, and it does not contradict ADR
  0002, whose point is that `.asd` means Ableton *looked at* the file.

## A warp guide is tagged after all — 2026-09-02

This ADR says a warp guide "is never tagged and never mixed". **The first half is not true, and
that is deliberate rather than a lapse.** crateside tags one with the key and no type — the
mapping `TaggingRunner.KeywordsFor` was read off the user's own library, where every drums entry
in staging already carried a key — and the user confirmed on 2026-09-02 that this is what they
want: so the key reads sensibly on the rare occasion a drum stem is selected in Ableton's
browser, and so the library is consistent. Never *mixed* still holds.

Found by noticing `Rob Dougan - Furious Angels (Drums).flac` carrying `Key|C` and `Key|Minor`,
and raised as a suspected bug. It was not one; the behaviour was already described in the
runner's own remarks. Recorded here because this ADR is where a reader would go to check, and it
said the opposite.

**The decision above is unaffected, and reads better for it.** The distinguishing axis was
already stated here as **retention, not role** — and tagging turns out to be a third thing that
is neither. A warp guide is left out of completeness because it is going to be deleted, not
because it lacks a key, and `StagingSong.IsFullyTagged` would now give the same answer whichever
reason it used. What needed correcting was the *reason* written beside the code, which said
"never tagged" where it meant "never kept".

**Outstanding:** keytag's `CONTEXT.md` carries the same "never tagged" wording in its *warp
guide* definition. That file is a glossary of current fact rather than a history, so it wants
the wording corrected in place rather than a note appended — in the other repo, and not done
here.
