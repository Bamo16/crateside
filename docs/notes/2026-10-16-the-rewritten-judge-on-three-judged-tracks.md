# The rewritten judge on three judged tracks

*Note, 2026-10-16, frozen. The check for [#111](https://github.com/Bamo16/crateside/issues/111)
(the judge rewrite) and [#112](https://github.com/Bamo16/crateside/issues/112) (pages fetched before
the judge runs), cut from ten tracks to three by the maintainer to save quota; decides nothing
itself. Run from an export of `master` at b565fe5: each track downloaded again, detected with the
ported detectors (#113), judged by `VerdictRunner` on Sonnet into a throwaway ledger. The old
sessions are the Claude runs that recorded each track's current verdict, through keytag's
`/detect-key`.*

## Agreement with the recorded verdicts

The comparison is agreement, not accuracy.

| track | recorded | rewritten judge | the new run's sources |
|---|---|---|---|
| Rob Araujo – *Nineteen* | F minor, MEDIUM | F minor, MEDIUM | none: nothing fetched, two searches found only algorithmic sites |
| Nelly – *Hot In Herre* | E Dorian, HIGH | E Aeolian, HIGH, with sections Intro/Verse E Dorian and Chorus E Aeolian | the fetched Hooktheory and Musicnotes pages, no search |
| GYARI – *アカリがやってきたぞっ* (*Akari ga Yattekita zo*) | G Dorian, HIGH | G Aeolian, MEDIUM | none: ChordWiki returned 403, so its snippet was not counted |

- **The tag agrees on all three** (*measured*).
- **Confidence dropped on GYARI** because the recorded HIGH rested on a ChordWiki page the new
  session could not open, and the prompt counts only pages it read (*measured*).
- **Neither run named Dorian as the primary mode.** On *Hot In Herre* the Dorian verse became a
  section, as Hooktheory analyzes it (*measured*).
- On *Nineteen* keydetect now reads A# minor (r 0.676, no backer), where the old evidence agreed
  with the verdict. The judge overruled it from the cadence and `edma` (*measured*).

## Tokens and turns

Input counts every token sent, cached or not; usage summed once per request ID.

| track | old: requests, input, output | new: requests, input, output |
|---|---|---|
| Nineteen | 6, 312k, 16.0k | 5, 192k, 8.3k |
| Hot In Herre | 6, 263k, 5.4k | 2, 69k, 6.5k |
| GYARI | 6, 238k, 7.5k | 5, 183k, 5.6k |

- **Every run used fewer tokens** (*measured*). The saving is largest where the fetched pages
  answered the question: *Hot In Herre* made no search and wrote its verdict on its second request.
- **Cache writes**, the costliest part of input, were 61k, 49k and 28k before and 43k, 32k and
  34k after (*measured*).

## The pages fetched before the session

- **Every site was reached on every track; nothing fetched was the wrong song** (*measured*).
  Only *Hot In Herre* was found, on Hooktheory and Musicnotes; Ultimate Guitar had none of the
  three.
- **Musicnotes' key was missed** on *Hot In Herre*: that product page uses an older layout. Fixed
  in 21b6a7e; the judge read the key from the page title instead.
