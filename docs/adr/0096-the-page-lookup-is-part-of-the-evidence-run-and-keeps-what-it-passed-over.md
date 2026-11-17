# 0096 - the page lookup is part of the evidence run, and keeps what it passed over

- Status: Accepted
- Date: 2026-11-16
- Amends [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md) (step 1
  of the verdict stage, *Look up the pages*, moves to the evidence run; an unreachable site is kept
  as such instead of leaving no `sources.json`).
- Extends [ADR 0077](0077-a-track-that-is-not-on-youtube-is-keyed-by-its-source-and-named-from-its-tags.md)
  (title-only candidates) and [ADR 0078](0078-the-person-corrects-which-pages-count-for-a-song.md)
  (page choices apply to the lookup's pages, wherever they are shown).
- Issue [#179](https://github.com/Bamo16/crateside/issues/179); design brief 23
  (`docs/design/briefs/23-evidence-before-judging.md`).

## Context

`SourcePages.FetchAsync` searches Hooktheory, Musicnotes and Ultimate Guitar and parses what it
finds, the Hooktheory tab included (#176). Nothing in it depends on a verdict, but it runs as the
first step of the verdict stage (`VerdictByRules` and `VerdictRunner` both call
`PageLookups.LookUpAsync`). So a track that has its detector readings but has not reached the rule
pass has no pages, and the song page says "not looked up yet" for a lookup that could have run long
ago.

Two more things are lost today:

- **A site that could not be reached leaves no trace.** ADR 0076 skips writing `sources.json`
  then, so the next attempt asks again. The song page cannot tell "Hooktheory was down" from "not
  looked up yet".
- **What the search passed over is thrown away.** Each site's search returns several entries;
  the lookup keeps the pages it confirmed and, for Hooktheory, up to a few title-only candidates.
  The rest are discarded, so nobody can see how many unrelated entries a search returns, or whether
  artist, title or duration could sort them without a judge. When the judge then cites a page the
  lookup never found (Yoko Shimomura's *Beware the Forest's Mushrooms*, filed as `forest-maze`; the
  coordinator counted three more among judged tracks citing Hooktheory: *Single Ladies*, *Juicy*,
  *Runaway*), there is no record of what the search did return.

## Decision

### The evidence run looks the song up

The evidence run ends with the page lookup, once the detectors' evidence is complete (ADR 0068).
It is skipped for a track parked for a premium shortfall: the upload may be swapped, and a swap
gets its own evidence run. The rule pass, the judge and the person's verdict read what the
evidence run kept; none of them fetches pages, except to retry an unreachable site (below). The
Evidence job's summary names the pages: `keydetect C major (r 0.71), madmom C major, edma C major ·
Hooktheory 1 page, Musicnotes none, Ultimate Guitar 2`.

A track whose evidence was produced before this lands has no `sources.json`. Amended 2026-11-16:
the evidence stage picks it for the lookup alone (`EvidenceSelector`, the decision the workers, the
Tracks GPU line and `/api/stages` share), with no re-detection and without needing its audio. The
lookup writes `sources.json` whatever the sites answer, so the track is picked once; a lookup that
throws is a strike, and the stage's five strikes end it. The rule pass's own lookup stays as a
backstop.

### `sources.json` is always written

It is written after every lookup, each site with its status, including `Unreachable` with its
error. ADR 0076's rule stands in meaning: **a site that could not be reached is not an absence.**
The rules do not rule while Hooktheory or Musicnotes is `Unreachable`, and the rule pass retries
only those sites (one try per track per start of crateside, as now), rewriting the file when they
answer. The judge, when it runs, retries them once more before its session and is then given what
there is, with the unreachable site named in its page list.

A track with no artist and title to search with is written with each site `NotLookedUp` (a new
`LookupStatus`) and the reason, so the page says why rather than "not yet".

### Every search hit is kept

Each `SiteLookup` gains `Passed`: every entry the site's search returned that the lookup neither
fetched as a page nor kept as a title-only candidate, as the search gave it: URL, artist, title,
and the site's own duration or song length when it gives one, plus a one-line reason (*artist is
none the track credits*, *title differs*, *over the page cap*). Up to 20 per site, the search's
first page. **They are not fetched**, so keeping them costs no requests.

Passed hits are kept for measuring and for the person; **they are never given to the judge** and
never counted by a rule. The judge is handed what it is handed today: the effective pages and the
title-only candidates (ADR 0077, ADR 0078).

### Where it is kept

- **Before a run:** `sources.json` beside `evidence.json` in the working set, as now.
- **On a run:** the run's `Lookups` copies it when the run is recorded (rules, judge or person, as
  now), `Passed` included. Nothing new in the ledger schema: `Lookups` is already a JSON column.
- **Which page a verdict used** is not stored. A source on a run whose URL is in its `Lookups`
  was found by the lookup; one that is not, and is not a page choice, was **found by the judge**.
  The song page and the API derive it, comparing URLs without scheme, `www.` or a trailing slash.

### The API

`GET /api/tracks/{id}` returns `evidence` for a track with evidence and no run, read from the
working set: `decidedBy` null, `center` null, and the rules' sentence for why they left it open in
a new field `rulesOpen` (null on a run). Each lookup carries `passed`, and each source on a run
carries `foundBy`: `lookup`, `judge` or `person`. `docs/api.md` and `docs/openapi.json` change
together.

## Consequences

- A track's pages are there as soon as its detectors are, so the song page shows the whole of the
  evidence before any judging, and a long judge queue no longer hides it.
- The judge's prompt is unchanged in what it carries; a judge never refetches a page the evidence
  run already read.
- Pages found by the judge and not by the lookup become countable from the ledger, run by run,
  which is the measure for improving the lookup.
- The evidence run is no longer purely local: it makes up to about a dozen requests per track
  (three searches, the pages, Hooktheory's section scores). It still contends for nothing scarce.
  `CONTEXT.md`'s **Evidence run** gains "and looking the song up on the sites"; ADR 0076's verdict
  stage starts at the rules.
- A working set discarded before any run (a track imported without a verdict) loses its lookup
  with its evidence, as it loses the readings today.
