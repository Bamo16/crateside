# 0085 - a track, or a stretch of it, can be marked unfit for measuring detectors

- Status: Accepted
- Date: 2026-11-08
- Sits beside the track marks of [ADR 0082](0082-the-persons-decisions-live-in-the-ledger-database.md).
  Reads bars as [ADR 0072](0072-bars-are-numbered-as-lives-ruler-numbers-them.md) numbers them.

## Context

The harnesses score detectors against two sets. The corpus is curated and every oddity in it is
described. The library is hundreds of tracks the maintainer has warped, and nobody has looked at
them one by one. A track that slows down and turns to chaos near its end (BIRDBRAIN) pulls an
aggregate around while its warp still follows it, and no algorithm should be judged on that
stretch. The track marks (*Changes key*, *Odd bars*) are yes/no and record no place, so they cannot
say "from bar 183". (#188)

## Decision

- **A new mark, "not fit for measuring", beside the others, but not a `TrackMark`.** Those are one
  row of a flag per song; this one carries places and reasons. It has its own table,
  `MeasuringExclusions`, keyed by `Song` (`NOCASE`) as the marks are, so it survives a rename and
  follows a video ID onto its song (ADR 0071) by `FollowSongs`. It is not a Live Collection.
- **It covers the whole track or a list of stretches, never both.** Marking the whole track replaces
  any stretches; adding a stretch to a marked track leaves only the stretches. **A reason is
  required** for the whole track and for every stretch.
- **A stretch is stored in seconds of the source file**, which is what the harnesses compare
  against, **and as typed** (`183–190`), so the maintainer's own words come back.
- **Bars are typed in Live's ruler (ADR 0072) when the track has a warp, in `m:ss` when it has
  none.** The warp is the track's shared warp (`WarpGroup`, as the warp view reads it).
  - `183–190`: from the start of bar 183 to the end of bar 190.
  - `183.3–190.2`: from bar 183 beat 3 to the start of bar 190 beat 2. `bar.beat.sixteenth` is
    accepted too, as Live's Start field writes it.
  - `start` and `end` are the file's edges, and the end is clamped to the file's length. `-4–2` is
    bars before beat 0; there is no bar 0.
  - Without a warp: `2:22–3:39` or plain seconds. Bars are refused.
  - The track page shows a stretch as bars where a warp exists and as `m:ss` otherwise, with the
    time beside the bars, recomputed from the stored seconds under the warp as it stands. A bar
    range is turned into seconds once, when it is added; a later re-warp does not move it.
- **Set and cleared on the track page**, in the tags group under *Odd bars* (`MeasuringExclusionStrip`):
  *Mark* opens a form (whole track or a stretch, the range, the reason); each stretch has *Remove*
  and the mark has *Clear*. Setting and clearing are recorded as `MarkSet` / `MarkCleared` in the
  action log.

## The API shape

For a harness, which should not read the code.

### `GET /api/measuring-exclusions`

Every marked track the app has a row for, ordered by `videoId`. A song with a mark and no row is not
listed.

```json
{
  "tracks": [
    {
      "videoId": "Zx3TrackA01",
      "song": "the examples - synthetic song",
      "wholeTrack": false,
      "reason": null,
      "stretches": [
        {
          "startSeconds": 364.0,
          "endSeconds": 380.0,
          "typed": "183–190",
          "reason": "Slows down and turns to chaos on every instrument"
        }
      ]
    },
    {
      "videoId": "Zx3TrackB02",
      "song": "the examples - another song",
      "wholeTrack": true,
      "reason": "A live recording with no steady tempo",
      "stretches": []
    }
  ]
}
```

- `videoId` is the track's work key (ADR 0077), the same key as `/api/tracks/{trackKey}`.
- `wholeTrack` true: leave the track out; `reason` says why and `stretches` is empty.
- `wholeTrack` false: leave out each stretch, `startSeconds` to `endSeconds` in seconds of the
  source file (the full mix a detector runs on, CLAUDE.md "A detector runs on the source"), and
  report how much was left out. `reason` is null.
- `typed` is for display; the numbers are `startSeconds` and `endSeconds`.
- A track not listed is not marked.

### In the track

`GET /api/tracks/{trackKey}` carries the same record at `records.measuringExclusions`, by the key
the store holds it under (the song, else the video ID), as `records.marks` is:
`{ "song": …, "reason": …, "stretches": [ … ], "wholeTrack": … }`.

## Consequences

- The ledger has one more table (migration `MeasuringExclusions`), backed up with the rest.
- Stretches are only as good as the warp they were typed against; a bar typed against a poor warp
  lands where that warp puts it, which is where Live shows it.
- The harness side (reading the endpoint, leaving the stretches out, reporting the share left out,
  the review list) is filed in the harness repos.
