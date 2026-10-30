# 0069 — A verdict is checked against its evidence before it is recorded

- Status: Accepted
- Date: 2026-10-28
- Amends [ADR 0055](0055-crateside-owns-the-key-judge-and-its-ledger.md): what a verdict file is checked for. Amends [ADR 0067](0067-a-judge-off-the-claude-limit-is-not-held-by-it.md): how long a rate-limited endpoint is held.

## Context

The judge sometimes runs on a model other than Claude (ADR 0067). In its first run of 32 verdicts it made mistakes a mechanical check would have caught. Will Stetson – *Why Do I* was recorded as C♯ minor while its own reasoning named A♯ minor as the tonic, and the evidence held only C♯ major and A♯ minor. Two verdicts got HIGH without what `judge-prompt.md` requires for it: DMX had no human source, Roxette had no backed detectors. **Measured:** the three cases were found by reading the 32 verdicts against their evidence. The check is judge-agnostic, so it belongs in crateside whatever model judges.

## Decision

After the ledger's own refusals, a recordable verdict file is checked twice more (`RecordTimeCheck`).

- **The center's key must come from somewhere.** It must be a key the session was given or wrote: a reading, runner-up, running key estimate or pairwise recommendation in the evidence (whole-track or window), a key on a page crateside fetched, a source's stated key, one of the verdict's own candidates, or a section's key. Spellings are compared as keys (enharmonics equal) and a mode collapses to its major or minor as the ledger does, so an A Dorian verdict is found by A minor. A WITHHELD verdict has no center and is exempt. A key found nowhere makes the file unusable: the session is resumed once with the problem named, and a second failure fails the run with that reason, which is a strike like any other failure.
- **HIGH needs what the prompt asks for it.** The prompt says HIGH is "evidence that could have failed independently supports the tonic, and nothing authoritative contradicts it: backed detectors, a tonic check in favor, and a human source that agrees." Three legs are checked: at least two of keydetect, madmom and Essentia name the center's key; the tonic check is not blank; a source of kind authoritative or corroborating (or the maintainer's ear, when the Ear setting counts it as a human source) states the center's key. A HIGH short of any leg resumes the session once, naming what is missing. If the second verdict still lacks it, it is recorded as MEDIUM with a closing paragraph of its reasoning saying it was capped and what it lacked. The key is not in doubt there, only the confidence.
- **One resume is shared** with the existing "write the verdict file now" resume: a session that has already been resumed for a refused file is not resumed again, so a HIGH it still cannot back is capped at once and a key it still cannot account for fails the run.
- **The endpoint's stated wait is honoured.** A 429 that states how long requests pause ("pause for 240 more seconds") holds the stage for that long, up to an hour, instead of the two-minute default; one that states none keeps two minutes. **Measured** in the 429 messages of the 2026-10-28 judge sessions: the stated waits were 109 to 900 seconds, counting down between retries; the other kind of 429 ("messages too frequent, try again later") stated none.

## Consequences

- **Amended 2026-10-28: the clause that nothing authoritative contradicts a HIGH is checked too.** A recorded source of kind authoritative whose `statesKey` is another key than the center's is a missing leg, resumed and then capped like the others, unless it is a Hooktheory page the work order marked `THIN PAGE` (matched by URL), as the prompt's fixed rule 1 exempts. Which sections a page covers is still not weighed. **MEASURED** in the re-judges of 28 October: Nicki Minaj – *Super Bass* was recorded HIGH on B major over its own recorded Hooktheory source stating G# minor, which this catches. Daft Punk – *Get Lucky* is not caught: its HIGH run recorded only Hooktheory (B minor) and left out the Musicnotes F# minor its earlier runs held, and that page was not among those crateside fetched for it.
- "Backed" is counted on the center's key, so two backers agreeing against keydetect back a HIGH on their key; keydetect and one backer agreeing is enough, as in the prompt's backing table.
- A tonic check is required to be present, not to be right.
- Evidence with no detector readings can no longer carry a HIGH.
- Stages other than the verdict stage and the spend-down loop do not use the stated wait; another stage waking the verdict stage can retry sooner. Amended 2026-10-30: it no longer can, and a 429 stating no wait backs off from a minute instead of keeping two ([ADR 0070](0070-a-refusing-endpoint-holds-judging-with-a-backoff.md)).
