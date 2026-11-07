# 0081 - the person removes a recorded human source

- Status: Accepted
- Date: 2026-11-07
- Extends [ADR 0078](0078-the-person-corrects-which-pages-count-for-a-song.md) (the person
  corrects which pages count for a song).
- Amended 2026-11-08 by [ADR 0082](0082-the-persons-decisions-live-in-the-ledger-database.md): the
  removal is kept in the ledger database, in `SourceRemovals`, not in `page-choices.json`.

## Context

A verdict's human sources are not all lookup pages. Some are written into the ledger from outside,
such as the analyses imported from the keydetect-harness corpus, and one can be wrong for the song
it is filed under. ADR 0078 lets the person reject a lookup page, but an imported source is not a
page the lookup returned, so there was nothing to reject.

## Decision

**Remove** on a source in the song page's Human sources list stops counting it for that track. The
source stays in the list, struck through and marked "Removed by you · not counted", with an undo.

- A source is identified by its address, else by how it was found (its `via`). A source with
  neither cannot be removed.
- The removal is kept in `page-choices.json` beside the page choices (ADR 0078), under the track's
  record as `removed`, with when it was made and when it was undone.
- The ledger run is not edited. The removal is applied where the evidence is read, for the song page
  and the API, which mark the source `isRemoved`. The line under the heading ("2 of 3 sources agree")
  counts only the sources that remain.
- The recorded verdict stands and is not ruled or judged again: nothing the rules or the judge read
  includes a recorded source, only the lookup's pages.

## Consequences

- A wrong imported source can be taken off a song without touching the ledger.
- A removal is not in the ledger, so a restored ledger backup does not restore it (as for ADR 0078).
