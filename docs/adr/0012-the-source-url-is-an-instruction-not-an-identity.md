# 0012 — the source URL is an instruction, not an identity

- Status: Accepted. keytag's half is built; crateside's is not.
- Date: 2026-09-09
- Pairs with [keytag ADR 0008](../../../keytag/docs/adr/0008-a-restatement-is-not-a-run.md),
  which was written in reply to this repo and settles the other half.

## Context

A track is in the library from a bad upload and the user finds a better video of the same song.
They want it separated from the new one, keeping the key, the BPM and the verdict that cost a
Claude session to produce.

Two questions had to be answered and they belong to different repos:

1. **Is this video ID that song?** Without an answer the stems — which carry the new video ID in
   the provenance StemForge writes — resolve to no verdict at all, and `VerdictSelector` judges
   the track again to re-derive a key already known.
2. **Which URL should crateside fetch?**

The first was put to keytag as `docs/swapping-a-source-video.md`, now deleted (see *Standing*).
keytag answered it with a **restatement**: a line carrying `restates`, the existing song
identity, the new `source_url`, and the restated run's verdict copied unchanged. Written only by
`keytag-restate`, and invariant-checked over the whole file on every run.

The design turned on a test in this repo. `A_line_carrying_no_verdict_would_take_the_songs_key_away`
showed that append-only plus latest-wins cannot distinguish *says nothing* from *says no key* —
so a bare alias resolves the new ID and blanks the song's key at the same moment, through the old
video ID as well. keytag chose a fully-formed line over a discriminated-but-empty one on exactly
that ground: forgetting a filter should be harmless, not destructive.

## Decision

**Song identity is keytag's. The URL to fetch is crateside's, and it is a standing instruction.**

The override lives on `TrackInstruction`, beside `Name`, `IsHeld` and `IsInstrumentalRelease`,
and `WorkOrder.Compose` prefers it over the playlist entry's own watch URL.

That is the right neighbor rather than a convenient one. Those fields are already the things the
user has *said* about a track as against the things that are *true* of it, and by
[ADR 0010](0010-the-playlist-is-the-inbox-not-the-work-list.md) they are read by video ID and
outlive the playlist entry — which a swapped source must also do, since the entry still names the
old video.

**The playlist entry is not rewritten and the new URL is never added to it.** The entry remains
how the work arrived; the override says what to fetch when the work runs.

## Consequences

**Nothing in crateside's ledger reading changes.** Checked rather than assumed, against a real
line obtained from `keytag-restate --dry-run`:

- Every field `LedgerRunDto` deserializes is populated on a restatement, and `restates` is
  ignored as an unknown property. Both hops of the video-ID lookup resolve, through the new ID
  and the old one alike, to the same verdict.
- `HasRunFor` becomes true for the new ID, so no second verdict run is spent.
- `KeyLedger.Runs` gains an entry, and its only consumer is `ProcessedTracks`, which folds it
  into `VerdictWithoutStems`. That set is used solely to filter items that came from the
  playlist, so an ID that is not in the playlist cannot become work. Inert.

**Skipping restatements when resolving the latest verdict is not needed.** keytag offered it;
declined, because the restated verdict is identical to the one it supersedes, so the distinction
buys nothing a reader can observe. `LedgerRun` gains no `IsRestatement` until something wants to
show one.

**`LedgerText.Repaired` stays load-bearing, contrary to the reply.** keytag's text-integrity rule
(its ADR 0007) repaired the damaged *names* and refuses new ones, but 18 mojibake sequences remain
in `reasoning` and `tonic_check`, which is where `RunEvidence` applies the repair. Pinned by
`The_prose_fields_still_need_repairing_on_the_way_out`, which skips itself if the ledger is ever
cleaned.

*Reversed 2026-09-09, the same day, once the file was cleaned.* keytag swept the prose
([its ADR 0007 addendum](../../../keytag/docs/adr/0007-text-integrity-is-part-of-the-ledger-contract.md)):
99 strings over 42 lines, and `LedgerText` is deleted. It was not merely left with nothing to do.
`Repaired` round-trips the **whole string**, so a sentence holding damaged text *beside* a
character CP1252 cannot encode — a real `→` — fails and is returned untouched. Two strings in this
ledger were that shape, and read-time repair left both broken where the swept file has them right.
The count above was correct when written and is why the sweep happened; the conclusion drawn from
it was not, because it assumed the repair could do the job it was being kept for.

**Ordering.** The restatement must land before any stem cut from the new video does, or there is a
window in which those stems resolve to nothing.

**The old video ID goes on resolving,** and should: stems cut from it are still that song. A song
simply accrues source URLs.

## Not built

The override field, the `WorkOrder.Compose` preference, the call to `keytag-restate`, and
whatever the page offers for pasting a better URL. The rest of the pipeline needs no work — a
swap discards the working set and reopens separation, handoff and tagging exactly as a re-cut
does, and `SeparationRunner` already passes the source URL when it holds no audio.

Two questions are open and neither blocks the above: whether crateside should ever *propose* a
swap rather than accept one, which is a much larger problem; and whether a swapped track should
look any different in the row, given `DisplayName` is stable across a restatement because keytag
copies `artist` and `title` verbatim.

## Standing

`docs/swapping-a-source-video.md` was deleted when this was written, at keytag's request and with
agreement. It framed the question, measured the trap and got a decision, and that job is done —
a standing document describing an undecided design is the stale instruction
[keytag ADR 0005](../../../keytag/docs/adr/0005-instructions-carry-no-history.md) exists to
prevent. Everything durable in it is either here or in keytag ADR 0008.
