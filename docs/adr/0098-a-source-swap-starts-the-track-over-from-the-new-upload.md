# 0098 - a source swap starts the track over from the new upload

- Status: Accepted
- Date: 2026-11-17
- Extends [ADR 0071](0071-a-record-kept-under-an-upload-follows-it-to-its-song.md) (a verdict
  follows the song to its new upload) and
  [ADR 0096](0096-the-page-lookup-is-part-of-the-evidence-run-and-keeps-what-it-passed-over.md)
  (the page lookup belongs to the evidence run).
- Issue [#205](https://github.com/Bamo16/crateside/issues/205).

## Context

A swap restated the verdict, recycled the stems and asked for a new cut, and left everything else
as the old upload had it: the kept cover, the name StemForge resolved, the evidence and the page
lookups. A separation running at the swap failed, and counted a strike. STREETPOP swapped from
`-pj24dQYGRc` to `LcTV5ceBb_I` ended with the song `unknown artist - streetpop`: the playlist
upload's title named no artist, the judge wrote "Unknown artist", and that held name beat the
resolved one in `PageLookups.NamesOf`, so every lookup refused.

## Decision

- **A swap drops the kept cover and the resolved name.** Both are fetched again from the new
  upload (`CoverArt.Forget`, `ResolvedNameStore.Forget`; the name worker resolves a key it holds
  no name for).
- **A swapped track owes detection** until evidence is taken after the swap
  (`CutRequests.OwesDetection`, read by `EvidenceSelector` beside the outstanding list). Once the
  handoff settles the request, history does not re-detect.
- **A restated verdict says which upload it was judged on**: `judgedOnVideoId` beside
  `sourceVideoId`. It is a marking, not a new judgement: nothing is judged again.
- **A separation whose retained download a swap threw away ends as canceled, "source swapped"**,
  not failed: no strike, and the track stays owed its cut.
- **"Unknown artist" is not a name to look up**: `NamesOf` falls through to the tags, the title
  and the resolved name.

## Consequences

A verdict judged on the earlier upload stays marked so until someone judges again. The song
identity the judge filed it under is not corrected by any of this.
