# 0078 - the person corrects which pages count for a song

- Status: Accepted
- Date: 2026-11-07
- Extends [ADR 0076](0076-crateside-rules-first-and-the-person-is-the-judge-of-the-rest.md) (the
  rules count Hooktheory and Musicnotes pages) and
  [ADR 0077](0077-a-track-that-is-not-on-youtube-is-keyed-by-its-source-and-named-from-its-tags.md)
  (title-only candidates are shown and never counted).
- Amended 2026-11-07 by [ADR 0081](0081-the-person-removes-a-recorded-human-source.md): a recorded
  human source can be removed too, and is kept in the same file.
- Amended 2026-11-08 by [ADR 0082](0082-the-persons-decisions-live-in-the-ledger-database.md): the
  choices are kept in the ledger database, in `PageChoices`, not in `page-choices.json`.

## Context

Tier A of ADR 0076 records HIGH on the strength of a page, so a page for another recording gives a
confident wrong key that nobody reviews. The opposite miss leaves a track the rules could have
settled: the *Super Mario RPG* track "Yoko Shimomura - Beware the Forest's Mushrooms" found nothing,
while Hooktheory files the song as
`https://www.hooktheory.com/theorytab/view/yoko-shimomura/forest-maze`. The person can tell which
page is right, so the song page lets them correct the lookup.

## Decision

Three actions in the song page's Human sources list:

1. **Not this song**, on a counted Hooktheory or Musicnotes page: the rules stop counting it.
2. **Count this page**, on a title-only candidate: the rules count it.
3. **Add a page by URL**, for a Hooktheory TheoryTab (`/theorytab/view/<artist>/<song>`) or a
   Musicnotes product (`/sheetmusic/<artist>/<title>/MN<digits>`). The page is fetched through the
   same reading `SourcePages` gives a found page, shown, and counted. Any other address is refused
   with a plain message, Ultimate Guitar included, since it is never counted. A page that states no
   key is refused too, since there is nothing in it to count.

Each can be undone. A rejected page stays in the list, struck through, with an undo.

### Where the choices are kept

**`%APPDATA%\crateside\page-choices.json`**, by track key, one file for every track. Not the ledger
and not the working set:

- The ledger records verdicts, one immutable run each. A choice is the person's standing opinion of
  a track's lookup, changed and undone between verdicts, and it would need a migration for a list
  that no other reader joins on.
- The working set is cleared by retention and the janitor, and holds `sources.json`, the lookup's
  own result, which a fresh lookup overwrites. The choices must outlive both.
- The roaming folder already holds the person's per-track decisions (`track-marks.json`,
  `genres.json`, `familiarity.json`) in the same shape, moved with the data root (ADR 0074).

The key is the track key, not the song identity: the lookup, the working set and the evidence are all
per track, and a swapped source keeps the track key. An entry holds the page's URL, the kind of choice
(`Rejected`, `Promoted`, `Added`), a snapshot of the page as the person saw it, when it was made and
when it was undone. An undone choice is kept with its `UndoneAt`, so "the pages changed since this
verdict" is a comparison of two times and survives an undo.

The snapshot is what lets a promoted or added page count without a fetch every time the rules run,
and lets a rejected page stay visible when a fresh lookup no longer returns it.

### The effective pages

The lookup's result is never edited. Everything that decides reads the **effective pages**: the
lookup's pages, minus the rejected ones, plus the promoted and added ones (marked as the person's),
with a promoted candidate no longer a candidate. The rules, the judge's prompt, the record-time check
of a judge's verdict, the choices the key picker offers and the pending panel all read them. A run's
`Lookups` stay what the lookup found; the song page applies the current choices when it shows them.

The rules treat a page the person counted like any other: Hooktheory's and Musicnotes' pages count,
and the source recorded says "counted at your say" or "added by you".

### After a change

By the latest verdict of the track:

| verdict | what happens |
|---|---|
| none | the rules run again at once; a ruling is recorded and the track goes on |
| `DecidedBy = Rules` | the rules run again, which costs nothing; a ruling is a new run, the old one stays in history as in any ledger re-run. When they no longer rule, the old verdict stands and the page says so |
| `Judge` or `Person` | left alone, never re-judged. The song page says the pages changed after that verdict |

### The judge

A judge session sees the effective pages, plus a short note in the page list naming each page the
person rejected, and marking each page the person promoted or added.

## Consequences

- A wrong page can be removed and a missed one added without waiting for a judge, and the HIGH that
  stood on the wrong page is replaced by a new run.
- A verdict by the judge or the person is never silently replaced, so it can stand on pages that no
  longer count; the song page says so.
- The choices are not in the ledger, so a restored ledger backup does not restore them.
