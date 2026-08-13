# Context — crateside

Glossary for the helper bench that supports the **human-limited steps** of the
user's Ableton mashup workflow. Glossary only — no implementation detail.

Terms shared with the detection tool — **Track**, **Stem**, **Song identity**,
**Verdict**, **Source video ID**, **Queue**, **Fully processed**, **Sample
Staging**, **Mashup Samples** — are defined once in `keytag`'s `CONTEXT.md` and
are not repeated here. This file covers what is specific to crateside: the
mashup-pairing vocabulary, and the views.

## Pairing

The domain of "what else can I play over this?". A **source key** is the key of
the thing the user already has in hand; everything below describes candidates
against it.

- **Partner** — a key that pairs acceptably with the source key. Partnership is a
  relationship between *keys*, not between tracks: a partner names a key to go
  looking for, and the looking happens in Ableton's browser.
- **Shift** — how many semitones a partner must be pitch-shifted to sit with the
  source, signed, normalized to ±6. **The dominant cost in the model**, and the
  first sort key: an unshifted pairing can be auditioned in seconds, whereas
  anything beyond ±1–2 semitones usually sounds wrong, so a small shift beats a
  close harmonic relationship. When several transpositions reach the same partner,
  the one with the smallest magnitude is the shift.
- **Alt shifts** — the other transpositions that reach the same partner, a fifth
  away in either direction (±7, normalized). Alternatives, not extras: use one
  *instead of* the shift, when it suits the material better.
- **Circle distance** — how many steps apart two keys sit on the circle of fifths,
  wrapped so it never exceeds 6. The harmonic half of the ranking, subordinate to
  shift.
- **Relationship** — what a partner *is* to the source in musical terms: the same
  key, its relative, a step around the circle in either direction, or a borrowing.
  Distinct from **shift** and **circle distance**, which are costs; this is a
  description.
- **Camelot code** — the harmonic-mixing notation (`8A`, `5B`, …) that numbers the
  circle of fifths and letters the mode. An alternate spelling of a key, not a
  separate concept.

## Views

- **Staging board** — the live answer to "what is in front of me right now?": every
  track in **Sample Staging**, its key and BPM, and how far through the
  human-limited steps each of its stems is. Entirely derived — it holds no state of
  its own and nothing on it is marked done by hand.
- **Partners** — the interactive answer to "what else can I use with this?".
  Returns **keys** to filter for, never files, because Ableton Live Lite admits no
  extensions and the filtering therefore happens in Ableton's own browser.
- **Queue view** — the **Queue** as work: what is in the playlist, what is already
  **fully processed**, and what is new.

## Orchestration

What crateside does when it stops only *showing* the workflow and starts *running*
it. **Evidence** and **Verdict** are keytag's terms and keep their meanings here;
the two run types below are named after which of them they produce.

- **Work order** — crateside's instruction for one track: which URL is canonical,
  where its stems belong, what quality to ask for. The thing that replaces a
  hand-written prompt. A work order says what should happen to a track next; it is
  not a record that anything did.
- **Evidence run** — the stage that produces the **evidence** for a track:
  acquiring the audio and running the detectors. Deterministic, needs no judgement,
  and contends for nothing scarce.
- **Verdict run** — the stage that turns evidence into a **verdict**. The only
  stage that requires an agent, because weighing evidence and going to look for
  human corroboration is the only part of the workflow that genuinely needs one.
- **Batch** — the set of tracks crateside selects to separate in one go. A batch is
  a **separation** unit specifically: it exists because separation is serial and
  hours long, so it is the thing the user chooses a moment for.
- **Stage** — one of evidence run, verdict run, or separation. Stages are named
  separately because each contends for a *different* scarce resource — nothing,
  Claude quota, and the GPU respectively — and so each wants its own moment to run.
  Being ready for one stage says nothing about being ready for another.

## Flagged ambiguities

- **"Vibe"** — the existing cmdlet's label for a partner's **relationship**
  (`Perfect Match`, `Relative (Epic)`, `Energy Rise`, `Relaxed`, `Mood Shift`,
  `Modulation`). It is lossy: `Modulation` is a catch-all for every relationship
  that is not the tonic, the relative, or one step around the circle — so it covers
  musically distinct borrowings, and it is the label on nearly everything once a
  shift is involved. **Resolution: prefer "relationship" as the term**, and say
  what each actually is rather than bucketing it. Retiring `Modulation` is a
  behaviour change from the cmdlet and should be a deliberate one.
- **"Sample"** — avoid. In DJ usage it means any asset used in production, which is
  too broad to be useful. Use **Track** for source material and **Stem** for a
  separated part. `Sample Staging` and `Mashup Samples` keep the word only because
  they are existing folder names on disk.
