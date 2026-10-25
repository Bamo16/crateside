# 0003 — crateside may write Ableton's tag store

- Status: Accepted; the library rule narrowed by [ADR 0039](0039-crateside-renames-in-the-library-and-warns-about-sets.md) on 2026-09-27 (renames may write it)
- Date: 2026-08-14
- Supersedes the first decision of keytag ADR 0001 ("do not write the Ableton XMP"),
  on measurement. That ADR's other decisions stand.

## Context

keytag ADR 0001 decided that no tool should write Ableton's
`Ableton Folder Info\<uuid>.xmp`. It gave three reasons:

1. *"The user keeps Ableton open at all times, and Ableton owns/rewrites that file while
   running (an external edit would be clobbered)."*
2. *"The user is also uneasy about a tool modifying it."*
3. *"The tagging step is a deliberate, hands-on part of their warp workflow, not a click
   worth automating away."*

**Reasons 2 and 3 were the user's, and the user withdrew them on 2026-08-14** — noting
also that 3 was never independent, but derived from 2: they had said they did not want it
automated, and an agent wrote that preference up as a workflow principle.

That left reason 1 alone, and reason 1 had **never been tested**. The word "owns" was
doing a great deal of work, and "an external edit would be clobbered" was an inference
stated as a fact. This is the same failure mode as ADR 0001's GPU premise and ADR 0002's
`.asd` reading, so it was measured rather than argued.

The prompt was a real problem: the user has mis-tagged a stem by hand and caught it only
by chance. If crateside can read the tags it can flag disagreement with the ledger — and
if it can *write* them, that class of error disappears.

## How it was measured

An isolated watched folder, `Crateside XMP Test`, with five copied stems — no risk to
either real root, and fully reproducible content. Both real XMPs were backed up first.
Ableton 12.4.3, running throughout except for the final step. Each step was performed by
the user in the UI or by crateside on disk, never both at once, and the file was read
back after each.

## What was found

1. **The file is not locked.** An external write succeeded with Ableton running. "Owns"
   is false in the exclusive sense.
2. **Ableton picks up external edits without a restart**, and without the folder being
   re-added. In the final test it required *no interaction at all* — Ableton is watching
   the file, not re-reading it on selection.
3. **External tags reach the search index, not just the Tag Editor.** Filtering by key
   found an externally-tagged file that had never been clicked. This is the one that
   matters, because filtering by key in the browser is what the tags are *for*.
4. **Ableton merges; it does not rewrite from memory.** After an external edit, tagging a
   different file through the UI preserved every external entry *and their insertion
   order*, appending the new one at the end. A write from an in-memory index would have
   dropped the entries it did not know about.
5. **Disk is authoritative.** After the XMP was deleted and replaced with a hand-written
   one, every tag not in the new file vanished from the browser's filters. Ableton did not
   defend its own earlier writes.
6. **crateside can create the file from nothing** — directory included — for a file
   Ableton had never indexed, and Ableton honored it.
7. **Quitting flushes nothing.** The file was byte-identical after shutdown.
8. **A malformed keyword permanently pollutes the tag vocabulary.** A `Key|F#` written
   with ASCII `#` instead of U+266F was not rejected and did not fail — Ableton registered
   it as a *new user-created tag*, which then appeared as a second "F#" in the Key group
   alongside the built-in one. Correcting the file did not retract it; it had to be
   deleted by hand, and only custom tags offer that option. **The vocabulary lives outside
   the folder XMP** — removing the tag did not modify the file at all.

## Decision

**crateside may write the Ableton tag store for the `Sample Staging` root.** Specifically:

- **Write the key and type tags it already knows** — the ledger verdict, and
  Vocal/Instrumental from the stem's own provenance.
- **Validate every keyword against the exact vocabulary before writing.** Sharps are
  **U+266F**, never ASCII `#`. Finding 8 makes this non-negotiable: a typo is worse than a
  no-op, because it silently creates a permanent bogus tag that only the user can remove.
- **Match Ableton's file format byte-for-byte**: UTF-8, **no BOM**, and **no XML
  declaration** — Ableton's own file has none, and a writer that adds one is not
  round-tripping.

  *Corrected 2026-08-20, on building it: "byte-for-byte" overstates what is achievable and
  what is needed.* Ableton's XMP toolkit wraps the four namespace declarations on
  `rdf:Description` one per line while writing every other element's attributes inline, and
  `XmlWriter` offers only all-or-nothing. Matching that would mean hand-rolling an XML
  writer, where an escaping bug would cost far more than the cosmetic difference it fixed.
  **The rules that are load-bearing are UTF-8, no BOM, no declaration, LF endings and a
  trailing newline** — all measured off the real file and all met. Finding 5 is what makes
  the rest cosmetic: disk is authoritative and Ableton honored a hand-written file, and by
  finding 4 it reformats to its own style on its next merge anyway. The round-trip test
  asserts entries, order and keywords survive, plus those five byte-level rules.
- **Read-modify-write**, preserving entries crateside did not create; create the directory
  and file when absent.
- **`Sample Staging` only. Never `Mashup Samples`.** The finished library is the user's,
  holds hand-applied tags crateside knows nothing about, and has no reason to be written.

**The user does not hand-tag in staging.** Their rule, offered when the question was still
open: staging holds only files bound for `Mashup Samples`, the only tags they ever apply
there are key and vocal/instrumental, and crateside can do both. Anything special — an
extra tag, a correction — means moving the file to `Mashup Samples` first, where crateside
never writes. This makes the concurrent-write question *moot* rather than solved, which is
better: the tests above never had both writers active simultaneously, so that is the one
scenario still unmeasured.

*Corrected in place 2026-08-20: this said "staging holds only **stems**" and "moving the
**stem**".* [ADR 0004](0004-sample-is-the-role-word-and-stem-is-the-origin-word.md) settled
the vocabulary the day after this was written, and by it *stem* names an origin — a
separated part of a track. **An instrumental release's source track is a sample and no kind
of stem at all**, and one of those is now in staging, so the narrower word made this rule
read as not covering it. The wording is load-bearing here because the rule is a contract
about what may be written.

*What that unmeasured scenario turned out to be — 2026-08-20:* the rule makes contention
moot only for **hand-tagging**. Moving a file out of staging to `Mashup Samples` in
Ableton's browser is not hand-tagging, and plausibly rewrites staging's tag store as a side
effect — a second writer the rule does not reach. Unmeasured still, and now known to be
reachable without the user breaking any rule.

## Alternatives considered

**Keep the prohibition and only flag disagreement.** The read-only half of this, already
sanctioned by ADR 0001 itself ("the record and the user's Ableton tag can diverge until an
XMP-read reconciliation feature exists — which is fine, and is itself the *was the tool
right?* signal"). **This is still being built**, because it is valuable whether or not
crateside writes: the user may still tag by hand in `Mashup Samples`, and a supersede in
the ledger can put a correct tag out of date. Rejected only as a *replacement* for
writing — it detects the error class instead of eliminating it.

**Write only while Ableton is closed.** The conservative reading, and what the evidence
would have supported had finding 4 gone the other way. Unnecessary given findings 1–7, and
close to useless in practice: the user keeps Ableton open at all times, so crateside would
have to queue writes indefinitely.

**Write the portable `INITIALKEY` file tag instead** (keytag ADR 0001 decision 3). Solves a
different problem — metadata portability across DAWs — and does nothing for the Ableton
browser, which is where the filtering happens. Still worth doing one day; not a substitute.

## Consequences

- **Tagging leaves the "human-limited steps."** That term is defined in keytag's
  `CONTEXT.md` as warping, tagging and sometimes renaming, and it is part of what this
  whole project is organized around. Warping remains, and the user still opens the stems in
  Ableton, so the saving is the clicks and — more valuable — the elimination of a real
  error class. **keytag's `CONTEXT.md` needs amending**, along with ADR 0001 itself.
- **crateside now writes into the library.** `CratesidePaths` documented that it writes
  nothing; that is no longer true, and the boundary is now "reads everything, writes the
  staging XMP and nothing else."
- **The Staging board's "tagged" column becomes partly self-inflicted.** It has always
  reported what Ableton holds; some of that will now be crateside's own writing. It stays
  purely derived — it still reports the file, not an intention — but "tagged" stops being
  evidence that a *human* looked at it.
- **Untested: simultaneous writes.** Every measurement above was sequential. The user's
  no-hand-tagging rule is what keeps that from mattering; if that rule is ever broken, this
  ADR does not cover what happens.
- **Untested: file removal.** Adding a file was covered (finding 6); removing one, and the
  user's report that emptying a folder deletes the XMP entirely, were not. crateside
  already treats an absent XMP as an ordinary empty state, so the risk is low.
- The test folder `P:\RYAN\Ableton\Crateside XMP Test` and its five `XMPTEST-*.flac` files
  exist only for this and should be deleted, and the watched folder removed in Ableton.

## Postscript

Third decision in this repo found to rest on an unverified premise, after ADR 0001's GPU
claim and ADR 0002's `.asd` reading — and the third to be settled in minutes once someone
actually looked. Worth noticing that all three were *inherited* rather than invented: each
was written down confidently by whoever was there first, and then cited rather than
checked. A recorded decision carries its premise forward with the same authority as its
conclusion, and nothing in the format distinguishes the measured parts from the plausible
ones.

## The move-out, measured — 2026-08-20

The scenario left open above — Ableton writing the staging tag store as a side effect of the
user moving a file to `Mashup Samples` — was measured with a watcher polling both stores at
100 ms, logging size, hash and NTFS file id. (File id rather than creation time: Windows
preserves creation time across a quick delete-and-recreate, so it cannot tell a replace from
an in-place write.)

**A first attempt found nothing at all**, because the file moved was *untagged*. There was no
entry to remove and none to carry across, so neither store was touched. Worth stating on its
own: **moving an untagged file is not a tag-store write.**

Moving a tagged one — `Alan Gomez - Mission 15 (Vocal - Full).flac`, carrying `Key|E`,
`Key|Minor`, `Type|Vocal` — produced this:

9. **A move-out is two writes, about a second apart.** Staging first (16:37:37.276), then
   `Mashup Samples` (16:37:38.354). Not one transaction, and not simultaneous.

10. **The removal is surgical, and finding 4 extends to removals.** Staging went 19 entries to
    18: exactly the moved file's entry gone, the other 18 intact, **insertion order preserved**,
    no surviving entry's tags altered. `Mashup Samples` went 432 to 433, the entry **appended at
    the end** with byte-identical keywords. Both files changed by exactly 453 bytes, in opposite
    directions — the entry moved verbatim. Ableton is merging against what is on disk in both
    directions, not rewriting from an in-memory index.

11. **Both stores are written *in place* — same file id, no atomic rename.** This is the finding
    that constrains crateside. Shrinking staging's file from 9458 to 9005 bytes in place means a
    truncate-and-rewrite, so there is a window in which the file on disk is neither the old
    content nor the new. **A reader can see a torn file, and a second in-place writer can corrupt
    one.**

12. `xmp:MetadataDate` is bumped on both, to the second of each write.

### What this settles for the tag writer

- **crateside writes atomically** — temp file in the same directory, then replace. It can then
  never itself leave a partial file, and its write is one rename rather than an interleavable
  truncate-and-rewrite.
- **crateside's reads must tolerate a torn file.** Finding 11 makes this real rather than
  defensive: a parse failure means "Ableton is mid-write", so the correct response is to skip
  the tick and come back, never to treat it as a corrupt store worth rebuilding.
- **Compare-and-swap on last-write-time** before replacing, so a write built on a stale read
  becomes a retry instead of dropping whatever Ableton did in between. Cheap to retry, because
  the audit re-derives the work list every tick.
- **Do not gate on Ableton being closed.** Findings 2 and 3 already showed external tags are
  picked up live and reach the search index, so a gate would cost the user real time to avoid a
  window measured here at about a second, twice per move-out.

**Still unmeasured:** genuinely simultaneous writes, which remain hard to provoke deliberately.

*Not* unmeasured, though it was briefly written up here as if it were: **whether Ableton keeps
watching a file that crateside replaces rather than edits in place.** Finding 5 already settles
it — that test *deleted* the XMP and put a hand-written one in its place, which is a harder
identity change than a rename-over, and Ableton honored the new content. Confirmed again on
2026-08-20 when the tag writer's first real run replaced the file (its id changed) and the tags
appeared in a browser that was already open.

## The store is a write channel, not a read source — 2026-09-02

Found by two real-library tests failing, and run down because the numbers did not add up. The
user dragged three samples from `Sample Staging` to `Mashup Samples` in Ableton's browser
between 16:36 and 16:40: `Eminem - Medicine Ball` (vocal and instrumental) and
`Goreshit - Toromi Hearts 2`. Their drums were deleted, which is the ordinary workflow step.

Measured that evening, with crateside not running, so Ableton was the only possible writer:

13. **The tags are correct in Ableton and absent from the XMP of the folder holding the files.**
    `Mashup Samples` has 482 entries and none for those three. The browser shows the right Key
    and Type for all three, **before and after a full quit and restart**.

14. **Quitting wrote neither store.** Both files byte-identical across the quit — same length,
    same SHA-256, same mtime, `Mashup Samples` untouched since 15:40 and staging since 16:49.
    Ableton did write on quit: `Indexer.txt`, `Library.cfg`, `Preferences.cfg`, `analytics.db`.
    Just not the XMPs.

15. **Staging keeps entries for files that have left.** All five, still there. Ableton does not
    prune the folder store on a move-out; it updates its own index.

16. **The indexer saw the moves.** `Indexer.txt` logs all five files at exactly the move
    timestamps — to the second — each under its **`Sample Staging`** path.

**So the authoritative tag state is Ableton's own index, not the per-root XMP.** The XMP is a
lagging, partial mirror: it can lack tags Ableton holds, and retain entries for files that are
gone. Where that index lives on disk was not chased down; it was not needed to establish the
above, and finding it is a separate job.

**This contradicts findings 9 and 10 for the same action, and the contradiction is not
resolved.** On 2026-08-20 a single browser move-out wrote both stores about a second apart,
watched at 100 ms. On 2026-09-02 a browser move-out of three files wrote neither, over five
hours and across a quit. Both are direct observations of the same gesture. What differs between
them is **not known** — candidates include one file versus three, and the state of the target
root's index.

*Hypothesis, explicitly not measured:* crateside's tag writer **replaces** staging's XMP — temp
file plus rename, per this ADR's own decision — which changes the file id. Finding 5 and the
2026-08-20 confirmation establish that Ableton keeps **reading** a replaced file. Neither
establishes that it keeps **writing** one. If Ableton holds an identity for a store it owns, a
replaced file could silently stop being written, which would explain the asymmetry and would
mean crateside's own atomic write detached the mirror it reads back. The test is a move-out from
a root crateside has never written. Recorded as a thing to check, not as a finding.

### What this settles for crateside

- **Writing stays sound.** The tags on those three files are crateside's own, written into
  staging and rendered correctly by Ableton across a restart. Findings 2 and 3 stand.
- **Reading the XMP as "what Ableton holds" is unsound.** It is trustworthy only for tags
  crateside itself wrote into `Sample Staging` — which is most of what it reads, but not all.
- **`TagAudit`'s findings over `Mashup Samples` are the least trustworthy of the lot**, since
  that root is exactly where crateside never writes and the user tags by hand. A *disagreement*
  or *untagged* finding there may be an artifact of the mirror rather than a fact about the
  library. The tagging stage's work list is safer, being staging-only.
- **Location beats tags as a completion signal**, which is what
  [ADR 0002](0002-there-is-no-warped-signal.md) concluded by a different route. Across this
  evening the file's whereabouts were right every time the tag store was wrong.

## This ADR is about tags, not files — 2026-09-12

`TrackRename.CanRename` and the stem picker both cited this ADR for a rule it does not contain:
that crateside may not *delete* a file from `Mashup Samples`. What is decided here is narrower —
crateside writes the **tag store** of `Sample Staging` and never of `Mashup Samples`.

The file rule is real and is stated in
[ADR 0019](0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md), with its
own reason: those files are warped and carry `.asd` sidecars. Separating the two matters because
they were being weighed as one — the tag rule costs nothing in practice, and only the file rule
was making the picker inconvenient.

## The tag store is no longer the only thing crateside writes — 2026-09-19

This ADR is quoted elsewhere as "crateside writes exactly one thing". That reading is now false:
[ADR 0022](0022-an-adopted-sample-is-a-file-crateside-did-not-make.md) has crateside write a
`COMMENT` tag into a file the user made, so the file can be joined to a track at all.

It does not weaken what is decided here. The two writes differ on every axis that made this one
cautious:

- **The tag store** is Ableton's, shared, written continuously, and read back by a program that
  also writes it. The care in this ADR is about that contention.
- **An adopted file's comment** is written once, to a file the user has just handed over, at the
  moment they ask, and never again. Nothing else writes it and nothing races for it.

The rule that has not moved: crateside still never writes the tag store of `Mashup Samples`, and
still never deletes a file from it.

## The library's keys can be corrected — 2026-10-07

[ADR 0052](0052-crateside-corrects-a-library-key-to-its-verdict.md) lets crateside rewrite a library
file's key to its verdict, for files the user approves. The premise above, that the library holds
hand-applied tags crateside knows nothing about, no longer holds: the user tags by hand in neither
root. crateside still never deletes a file from the library.
