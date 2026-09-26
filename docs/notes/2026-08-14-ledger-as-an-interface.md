# The ledger as an interface, not a log

*Note, 2026-08-14, frozen. Names are as they were then; "the board" is the staging board, which has since become the Tracks view.*

**Status:** observations, not decisions. Input for a future revision of the `detect-key`
skill and of `runs.jsonl`'s schema.

`results/runs.jsonl` was designed as **an activity log** — keytag ADR 0001 settled it as
"the safest thing an agent can write", append-only, one line per run, serving recall and
tuning. At that point it had no consumer but a human and a `grep`.

It has one now. crateside reads it on every board build, and under ADR 0001 (this repo)
crateside is also the orchestrator, so it is the *only* consumer. That changes what the
schema is for: fields that were fine as prose-for-a-human are now the input to a view, and
the cost of an unstructured field lands on whoever renders it.

Everything below was found by writing that consumer. None of it is a complaint about the
original decision, which was right for what the file then was.

## What works and should not change

- **Song identity + append-only supersede.** The whole join rests on it and it holds.
- **`verdict`.** Every one of 127 runs spells the key as `<tonic> <major|minor>`, sharps
  only, no exceptions — so it parses without defence.
- **`bpm` as a triple** (`value` / `source` / `beatdetect`). Keeping the runner's own
  estimate beside the recommendation is what makes a conflict detectable at all.
- **`machine` carrying the raw runner output.** It is the bulk of every line and it is
  worth it — this is what makes an evidence view possible years later.

## Where it costs the consumer

### `sources[].tier` — three vocabularies and free prose

84 runs carry sources; their `tier` values include `gold`, `silver`, `bronze`, `tier-3`,
`tier-1/2 Hooktheory`, `tier-2 UG chord chart`, `tier-3 (self-inconsistent)`, and
`tier-3 -- discounted, likely a beginner-transposed arrangement, not the recorded key`.
Over 30 distinct values.

Nothing can switch on it. crateside classifies by **host** instead — stable, and
`SOURCES.md` already says what each host is worth — and renders the recorded string beside
it as the agent's own note. That works, but it means the field the schema calls `tier` is
not the tier.

*What would help:* a closed `tier` enum, with the prose moved to a `note` field. The prose
is often the most interesting part and should not be discarded — just separated.

### `sources[].url` is not always a URL

Two real values: `user-provided image` (the user photographed sheet music — there is no
page) and `open.spotify.com/track/… (via web search)` (a real link with no scheme and a
trailing annotation). A consumer that linkifies blindly produces dead anchors, so crateside
resolves what it can and renders the rest as text.

*What would help:* `url` null when there is no link, and a `via` or `note` field for how it
was obtained.

### `sources[].states` is prose — 79 of 97 are not just a key

Ranges from `Eb major` to a 191-character description of a piano arrangement's key
signature, bar range and tempo marking. So "what did this source say the key was" is not
machine-readable, and crateside cannot show agreement or dissent per source without the
user reading each line.

*What would help:* a `states_key` in the same `<tonic> <mode>` form as `verdict.key`, with
the prose kept alongside. That alone would let a view mark each source as agreeing with or
dissenting from the verdict — which is the single most useful thing the sources list could
show and currently cannot.

### `reasoning` is one prose blob — median 439 chars, max 1,734

It reliably contains several distinct things: the required chord/flicker tonic check, the
confidence rationale, often the BPM argument, and often how a dissenting source was
handled. All of it is worth keeping and it is genuinely well written, but as one string it
renders as a wall and nothing can be surfaced selectively — a view cannot show "the tonic
check" next to the chords, or "why not HIGH" next to the confidence badge.

*What would help:* splitting it along the seams the skill already imposes. The skill's step
4 mandates the chord/flicker note as its own 2–4 sentences, and confidence has its own
rationale — so the structure exists in the instructions and is flattened on the way out.

### `machine.pairwise_agreement` is undocumented

`results/README.md` describes `machine` only as "the runner's full `--json`". Building
against it meant guessing the shape, and the guess was wrong — `{status, recommended, note}`,
not `{agree, relation}` — which shipped a view that reported disagreement on runs where all
three detectors agreed.

*What would help:* documenting the fields a consumer is expected to read. The `status`
vocabulary is closed (`full` / `relative` / `none` / `single`) and worth stating, since
`relative` is the one outcome that carries a specific warning.

### Chord timing is discarded upstream

Not a schema problem but a data-loss one, found while investigating the above. ffmpeg emits
timestamped chord *change* events (`t=8.04 chord=D`), and the runner keeps only the final
summary string, so how long each chord held never reaches the record. Durations are what
distinguish a tonic from a passing chord — on one measured track the "key chord" by the
collapsed list held 9 seconds of 138.

Cheap to fix (`chord_events`, mirroring the existing `key_events`), and small: ffmpeg emits
changes, not samples, so it is tens of entries, not hundreds.

## The shape of the change, if it is made

The consistent theme is **one field doing two jobs** — a classification and a note, a link
and a provenance, a key and an explanation. Splitting each into a machine-readable part and
a prose part would cost the skill very little (it is already producing both, concatenated)
and would let crateside show what it currently has to make the user read.

Whether that is worth a schema revision, and whether older lines get migrated or the
consumer simply tolerates both shapes, is not decided here.
