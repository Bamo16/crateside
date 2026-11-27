# 0112 - Camelot tags are derived from the Key tags

- Status: Accepted
- Date: 2026-11-26
- Builds [#241](https://github.com/Bamo16/crateside/issues/241). Amends
  [ADR 0087](0087-crateside-tags-every-song-and-the-person-corrects-in-crateside-or-in-live.md)
  (a new group crateside writes everywhere it writes a key, corrected like the key rather than
  settled by the person). Decided by the maintainer on 2026-11-26.

## Context

To find a vocal for an A minor instrumental the maintainer filters `A` + `Minor` in Live's browser,
auditions, then swaps to `C` + `Major` and auditions again. The two keys share seven notes and
a Camelot number. Modal tracks (a D Dorian or G Mixolydian centre, which carry `Key|Dorian` etc.
beside their collapse) use the same seven notes too, and today appear under neither key's filter
together. The detector keeps reading the 24 keys; only the tags change (keydetect-harness #10,
the note-set study).

## Decision

- **A group of crateside's own, `Camelot`, with 14 tags: `1`-`12`, `A`, `B`** (`Camelot|8`,
  `Camelot|A`). Standard numbering: A minor is 8A, C major 8B, each step a fifth. Like `Meter`
  and `Genres`, the group exists in Live's browser once a file carries one, so only the tags in
  use show.
- **Derived from the Key tags, never set on its own.** `CamelotTags.Reconcile` reads the whole
  keyword bag a write is about to store, replaces every `Camelot|` keyword in it with the ones the
  bag's Key tags call for, and leaves a bag with no whole key alone. The tagging stage and the
  backing pass (both roots, key backing and key correction alike) go through it, so a corrected key
  rewrites both groups in one write, and a `Camelot|` tag that disagrees with the key, or one
  edited in Live, is overwritten the next time the file is written. A Camelot edit in Live is not an
  answer: the key is what gets corrected.
- **Major and minor: a number and a letter.** A minor `8`, `A`; C major `8`, `B`.
- **A modal centre: the number of its note set, and no letter.** The note set is the major
  scale the mode rotates from (D Dorian and G Mixolydian are C major's, `8`). No letter, because
  the tonic is neither A minor nor C major, so the exact filter `8` + `A` does not catch it. The
  collapse's own number (D Dorian collapses to D minor, 7A) is not used.
- **A second key centre that has its own Key tags** gets its own Camelot tags: `CamelotTags.For`
  takes several centres and returns the union. Nothing writes Key tags for a second centre today
  (ADR 0100 places one in the verdict only), so every file carries one number until something does.
- **Backfill is the backing pass.** Every keyed file in the library and Staging is rewritten with its
  Camelot tags on the next pass; there is no button.
- **The keyword is known to the writer** (`CratesideKeywords.IsKnown`: `Camelot|1`-`12`, `A`, `B`),
  so a file carrying them is not left alone as holding a keyword the writer would refuse.

## Considered options

- **The collapse's number for a modal centre.** D Dorian would read 7A beside D minor, the key its
  collapse names. It would put a modal track under a filter that finds music in another set of
  notes than the one it uses.
- **A letter on a modal centre.** `8` + `A` would then return D Dorian beside A minor, which is not
  an A minor track.

## Consequences

- Live's browser orders the numbers as it sorts any group's tags; whether `1, 10, 11, 12, 2...`
  bothers the maintainer is for him to say after the first sight of it.
- A file's tags change only by the key: a tag written by hand is overwritten, which the maintainer
  accepted.
