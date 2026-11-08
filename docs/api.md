# The API

crateside serves what its pages show as read-only JSON, so a script or an agent can ask what state a
track is in without reading `ledger.db` (which holds the person's decisions too), `jobs.jsonl`, the stores under `%APPDATA%\crateside` and
the `.asd` sidecars one by one. It is on the app's own listener, `http://localhost:5027` when started
from the `run` task, and like the pages it answers on this machine only.

Nothing here writes. Each endpoint calls the services the pages use and serializes their result, so
the API and the pages cannot disagree. A request first rebuilds the rows if they are more than 30
seconds old, as opening a page does.

## The schema

- **`/scalar`** browses every endpoint and type, and can send a request. Settings links to it.
- **`/openapi/v1.json`** is the OpenAPI 3.1 document the running app serves.
- **[`openapi.json`](openapi.json)** is the same document, committed. Building `src/Crateside` rewrites
  it, so an API change shows in the diff; CI does not rewrite it, and `OpenApiDocumentTests` fails
  there when the committed file is out of date.

Names are camelCase and enums are written by name (`"ReadyToWarp"`). Every property is written,
`null` included, so a missing field means the shape changed rather than that a value is absent.

A track's key is its **work key**: a YouTube video ID, or `<source>~<id>` for a track that is not on
YouTube (`soundcloud~242136202`, `file~3fa9c0d12be84471`; ADR 0077). Every `videoId` below holds
one, and `{trackKey}` in a path is one. A row also carries `release` (album, album artist, track and
disc numbers, genre, composer, organization, `ost`, a year with its `yearKind` of `Release` or
`Upload`, and `cover`'s file name) for a track taken in from a file or a site, and
`hasNearSilentVocal`, true when its vocal stem is more than 16 dB quieter than its instrumental.

## `GET /api/tracks`

The Tracks page's rows, narrowed by the page's own filters:

| query | means |
|---|---|
| `filter` | `Unfinished` (when not given), `InStaging`, `InFlight`, `NeedsAttention`, `TagsToAnswer`, `TempoDrifts`, `OffItsTempo` or `All`, spelled exactly so; any other value is a 400 |
| `search` | a name, key or video ID; like the page's search, it looks through every track and ignores `filter` and `familiarity` |
| `familiarity` | only rows whose familiarity is tagged as this |

`decidedBy` says who made a row's verdict: `Judge`, `Rules` (crateside's own rules) or `Person`
(chosen on the song page); `waitingForPerson` is true for a track the rules left open while no judge
is set up (ADR 0076). `NeedsAttention` includes such a track.

`Off its tempo` comes furthest off first; the others keep the page's order. A 503 means the rows
have never been read, and its `detail` says why.

The example is `curl "http://localhost:5027/api/tracks?filter=InStaging"` against a synthetic
track, the same state the API's tests stand on. Each row is the whole `TrackRow` the pages draw.

```json
{
  "rows": [
    {
      "videoId": "Zx3TrackA01",
      "detailsHref": "song/Zx3TrackA01",
      "playlistItemId": null,
      "title": "The Examples - Synthetic Song",
      "derivedTitle": null,
      "isRenamed": false,
      "statedSourceUrl": null,
      "isSourceSwapped": false,
      "samplesLeftInLibrary": 0,
      "uploads": [
        {
          "videoId": "Zx3TrackA01",
          "role": "cut from"
        }
      ],
      "hasSeveralUploads": false,
      "nameSource": "Files",
      "awaitingRename": 0,
      "renameScope": "1 file + folder info",
      "renameDetail": "Renames the files in Staging and their entries in Ableton's tag store, so the tags follow the name.",
      "verdict": {
        "timestamp": "2026-10-01T12:00:00+00:00",
        "song": "the examples - synthetic song",
        "artist": "The Examples",
        "title": "Synthetic Song",
        "displayName": "The Examples - Synthetic Song",
        "sourceUrl": "https://music.youtube.com/watch?v=Zx3TrackA01",
        "sourceVideoId": "Zx3TrackA01",
        "center": {
          "tonic": "F#",
          "mode": "Aeolian",
          "collapsed": "F# minor",
          "isModal": false,
          "name": "F# minor"
        },
        "mixed": false,
        "sections": [],
        "key": "F# minor",
        "mode": "Aeolian",
        "isCollapsed": false,
        "modalKey": null,
        "confidence": "High",
        "decidedBy": "Judge",
        "bpm": null,
        "restates": null,
        "hasTaggableKey": true
      },
      "lookalike": null,
      "song": "the examples - synthetic song",
      "genre": null,
      "familiarity": {
        "song": "the examples - synthetic song",
        "artist": "The Examples",
        "artistFans": 1200,
        "songRank": 50000,
        "lookedUpAt": "2026-10-01T12:00:00+00:00",
        "lookupVersion": 1,
        "settled": null
      },
      "marks": [
        "OddBars"
      ],
      "hasTagsToAnswer": false,
      "artistAndTitle": {
        "item1": "The Examples",
        "item2": "Synthetic Song"
      },
      "key": "F# minor",
      "confidence": "High",
      "decidedBy": "Judge",
      "bpm": null,
      "tempo": null,
      "beats": null,
      "tempoDrifts": false,
      "offItsTempo": false,
      "runningStage": null,
      "runningSince": null,
      "gaveUpAt": null,
      "gaveUpStrikes": 0,
      "gaveUpFor": null,
      "retrying": null,
      "deletions": [],
      "isLibraryOnly": false,
      "hasUpload": true,
      "plan": {
        "presets": [
          "vocal_full",
          "instrumental_full"
        ],
        "keepSource": false,
        "addedSources": [],
        "adoptedSamples": []
      },
      "isAddedByUrl": false,
      "isBackfill": false,
      "canForget": true,
      "parkedFor": null,
      "waitingForJudge": false,
      "waitingForPerson": false,
      "acquisition": null,
      "gpuPosition": null,
      "samples": [
        {
          "path": "C:\\Music\\Staging\\Synthetic Song (Instrumental - Full).flac",
          "label": "Instrumental - Full",
          "kind": "Preset",
          "presetId": null,
          "abletonKey": "F#m",
          "isKeyTagged": true,
          "isPremium": null,
          "isInLibrary": false,
          "hasDefaultClip": true,
          "warpBorrowedFrom": null,
          "tempo": null,
          "beats": null,
          "sourceVideoId": null
        }
      ],
      "hasWarpGuide": false,
      "folder": null,
      "stagedSince": null,
      "state": "ReadyToMove",
      "inventory": {
        "files": [],
        "isClassified": false,
        "presets": [],
        "hasSource": false,
        "isFinished": false,
        "hasWarpGuide": false,
        "unrecognized": []
      },
      "recipe": {
        "presets": [
          "vocal_full",
          "instrumental_full"
        ],
        "extractDrums": true,
        "keepSource": false,
        "format": "flac",
        "expectedOutputs": 3,
        "passes": 3,
        "estimate": "00:06:00"
      },
      "shortfall": {
        "presets": [
          "vocal_full",
          "instrumental_full"
        ],
        "keepSource": false,
        "addedSources": [],
        "adoptedSamples": []
      },
      "isShort": false,
      "canChangePlan": false,
      "isCutInFlight": false,
      "facts": {
        "videoId": "Zx3TrackA01",
        "song": "the examples - synthetic song",
        "key": "F# minor",
        "plan": {
          "presets": [
            "vocal_full",
            "instrumental_full"
          ],
          "keepSource": false,
          "addedSources": [],
          "adoptedSamples": []
        },
        "uploads": [
          "Zx3TrackA01"
        ],
        "isLibraryOnly": false,
        "hasVerdict": true,
        "hasUpload": true,
        "labels": []
      },
      "auditNote": null,
      "isAwaitingJudgment": false,
      "chipLabel": "ready to move",
      "chipStyle": "AmberFilled",
      "chipHasDot": false,
      "next": "warped — move to the library",
      "detail": "Judged F# minor, high confidence.",
      "retryNote": null
    }
  ],
  "readAt": "2026-10-01T12:00:00+00:00",
  "playlistProblem": null
}
```

## `GET /api/tracks/{trackKey}`

Everything crateside knows about one track, or a 404 with a problem body when it has neither a row
nor a verdict for the video ID:

- **`row`**: the track's row, the same object `/api/tracks` returns. Null for a song the ledger
  judged that has no row.
- **`evidence`**: the evidence behind the latest verdict: its sections, sources, windows, candidates
  and every detector's readings. Track details' *Why* section draws it. A source that is a
  TheoryTab carries the tab as the lookup parsed it (`tab`: its sections in order, each section's
  keys with the beat they start at, its sync span, and the share of the video they cover).
  `lookups` holds every page the lookup handed the judge, site by site, cited or not, so what the
  judge passed over can be read beside what it used. A lookup's `candidates`, when it has any, are
  pages with the exact title under no artist the track credits: unconfirmed, shown to the judge, and
  never a source for a rule. Runs recorded before 2026-11-01 have neither.
- **`jobs`**: every line of `jobs.jsonl` for any of the track's uploads, oldest first. A stage that
  finished well says what it did in `summary` ("D major, HIGH, 2 sections; 3 turns, 150 s"); lines
  written before summaries existed, and every line that is not a success, have none.
- **`stageNotes`**: what the two passes with no job record did to the track's samples, one line each
  in `summary`, oldest first: the warp pass (`stage` `Warp`: "Vocal - Full: starter warp at 190.00
  BPM, marker at 0.75 s", "Vocal - Full: Warp off: one tempo misses by 12 ms") and loudness (`stage`
  `Loudness`: "Vocal - Full: clip gain −3.2 dB to −14 LUFS"). The activity feed merges them in with
  the job lines.
- **`stems`**: each sample on disk, with the keywords and Collections Live holds for it (`tags`,
  null when it holds none) and its parsed sidecar (`warp`): whose warp it is (`owner.kind`), the
  markers in seconds and beats, the tempo read bar by bar, the default clip and loop in seconds,
  and whether Warp is on. Beside that, crateside's own records about the warp: the one it wrote that
  the sidecar still holds (`written`), the one it last wrote even if saved over (`lastWritten`),
  a retype to a truer tempo (`retyped`), and the copies of the sidecar it kept (`backups`). A
  starter written since 2026-11-01 says how it was made (`written.starter`): the tempo written and
  beatdetect's before refining, the marker in seconds and its beat within the bar (0 the downbeat),
  beatdetect's beat and downbeat with its confidence and the gate it was held to, where the beat
  came from (`beatFrom`: `ReadingRefinedByFold`, or `FoldAlone` when beatdetect gave none), how the
  bar was numbered (`barFrom`: `Downbeat`, or `UnsureDownbeat`, `NotInFours` or `NoDownbeat`, each
  making the marker beat 0), and the beat beatdetect's own downbeat would have given it
  (`readMarkerBeat`). The warp card shows the same as one line.
- **`records`**: the track's entry in each per-song store (genres, familiarity, marks, StemForge's
  resolved names, the user's standing instructions) under **every key it is filed under**: its
  video IDs and its song. The row shows the genre, familiarity and marks filed under `row.song`
  only, so a record under any other key is one the pages do not show.

The example below is `curl http://localhost:5027/api/tracks/Zx3TrackA01` for the same synthetic
track, with its `row` cut short. Its familiarity and marks are filed under the song, so the row
shows them; its genre was filed under the video ID, so `records.genres` holds it there and the row
shows no genre.

```jsonc
{
  "videoId": "Zx3TrackA01",
  "row": { "videoId": "Zx3TrackA01", "state": "ReadyToMove" /* … the whole row, as in /api/tracks */ },
  "evidence": {
    "timestamp": "2026-10-01T12:00:00+00:00",
    "song": "the examples - synthetic song",
    "artist": "The Examples",
    "title": "Synthetic Song",
    "displayName": "The Examples - Synthetic Song",
    "key": "F# minor",
    "center": {
      "tonic": "F#",
      "mode": "Aeolian",
      "collapsed": "F# minor",
      "isModal": false,
      "name": "F# minor"
    },
    "mode": "Aeolian",
    "mixed": false,
    "sections": [],
    "windows": [],
    "direction": null,
    "readings": [],
    "tonicCheck": null,
    "reasoning": null,
    "candidates": [],
    "withheldReasoning": null,
    "firstPass": null,
    "inputKind": null,
    "sources": [
      {
        "url": "https://www.hooktheory.com/theorytab/view/the-examples/synthetic-song",
        "via": null,
        "states": null,
        "statesKey": "F# minor",
        "statesMode": null,
        "recordedKind": "authoritative",
        "note": null,
        "sections": null,
        "covers": "whole song",
        "relation": null,
        "verdictKey": "F# minor",
        "tab": {
          "contributors": ["one", "two"],
          "sections": [
            {
              "name": "Verse",
              "progression": "i VI III VII",
              "percentChord": 100,
              "percentMelody": 50,
              "keys": [{ "beat": 1, "key": "F# minor" }],
              "chords": null,
              "harmony": [],
              "videoId": null,
              "syncStart": 0.1,
              "syncEnd": 0.3
            }
          ],
          "allKeys": null,
          "syncedVideoId": null,
          "coverage": 0.2,
          "isThin": false
        },
        "link": "https://www.hooktheory.com/theorytab/view/the-examples/synthetic-song",
        "host": "www.hooktheory.com",
        "stance": "Agrees",
        "kind": "Authoritative",
        "kindFromHost": "Authoritative",
        "kindDisagreesWithHost": false
      }
    ],
    "lookups": [
      {
        "site": "Hooktheory",
        "status": "Found",
        "pages": [
          {
            "site": "Hooktheory",
            "url": "https://www.hooktheory.com/theorytab/view/the-examples/synthetic-song",
            "heading": null,
            "arrangement": null,
            "keys": [{ "key": "F# minor", "section": "Verse", "tempo": 126 }],
            "note": null,
            "tab": { /* … the same tab as the source's */ }
          },
          {
            "site": "Hooktheory",
            "url": "https://www.hooktheory.com/theorytab/view/a-cover-band/synthetic-song",
            "heading": null,
            "arrangement": null,
            "keys": [{ "key": "G minor", "section": "Chorus", "tempo": 120 }],
            "note": null,
            "tab": null
          }
        ],
        "error": null
      },
      { "site": "Musicnotes", "status": "NotFound", "pages": [], "error": null }
    ],
    "algorithms": [],
    "lateReading": null,
    "agreement": [],
    "backers": null,
    "chords": [],
    "keyEvents": [],
    "bpm": null
  },
  "jobs": [
    {
      "id": "Zx3TrackA01:Download",
      "videoId": "Zx3TrackA01",
      "stage": "Download",
      "status": "Running",
      "timestamp": "2026-10-01T12:00:00+00:00",
      "processId": null,
      "processStartedAt": null,
      "error": null,
      "adopted": null,
      "expectedOutputs": null,
      "destination": null,
      "heldUntil": null,
      "summary": null
    },
    {
      "id": "Zx3TrackA01:Download",
      "videoId": "Zx3TrackA01",
      "stage": "Download",
      "status": "Succeeded",
      "timestamp": "2026-10-01T12:00:00+00:00",
      "processId": null,
      "processStartedAt": null,
      "error": null,
      "adopted": null,
      "expectedOutputs": null,
      "destination": null,
      "heldUntil": null,
      "summary": null
    }
  ],
  "stageNotes": [],
  "stems": [
    {
      "path": "C:\\Music\\Staging\\Synthetic Song (Instrumental - Full).flac",
      "label": "Instrumental - Full",
      "isInLibrary": false,
      "tags": null,
      "warp": {
        "owner": {
          "kind": "starter",
          "written": "2026-10-01T07:58:00-05:00"
        },
        "hasSidecar": true,
        "isWarped": true,
        "isDefaultClipSaved": true,
        "sampleRate": 44100,
        "seconds": 10,
        "markers": [
          {
            "seconds": 0.5,
            "beat": 0
          },
          {
            "seconds": 2.5,
            "beat": 4
          }
        ],
        "tempo": {
          "bpm": 120,
          "shape": "Drifting",
          "low": 120,
          "high": 120,
          "stable": [],
          "audibleBars": 4,
          "windows": [
            {
              "start": 0.5,
              "end": 8.5,
              "bpm": 120
            }
          ],
          "grid": null
        },
        "clip": {
          "start": 0.5,
          "end": 8.5
        },
        "loop": {
          "start": 0.5,
          "end": 8.5,
          "on": true
        },
        "transientOrigin": null,
        "isAuthority": false,
        "written": {
          "from": null,
          "length": 31180,
          "written": "2026-10-01T12:58:00Z",
          "isUnwarped": false,
          "starter": {
            "bpm": 120,
            "readBpm": 119.998,
            "markerSeconds": 0.5,
            "markerBeat": 0,
            "readBeatSeconds": 0.5,
            "downbeat": { "seconds": 1.5, "confidence": 0.317, "beatsPerBar": 4 },
            "barFrom": "UnsureDownbeat",
            "downbeatSureAt": 0.6,
            "beatFrom": "ReadingRefinedByFold",
            "readMarkerBeat": 2
          }
        },
        "lastWritten": { /* … the same, until Live or the user saves over it */ },
        "retyped": null,
        "backups": [
          "C:\\Users\\you\\AppData\\Roaming\\crateside\\warp-backups\\2026-10-01 115900 Synthetic Song (Instrumental - Full).flac.asd"
        ]
      }
    }
  ],
  "records": {
    "genres": {
      "Zx3TrackA01": {
        "song": "Zx3TrackA01",
        "genre": null,
        "isSettled": false,
        "suggested": null,
        "heard": [
          "synthwave"
        ],
        "lookedUpAt": "2026-10-01T12:00:00+00:00"
      }
    },
    "familiarity": {
      "the examples - synthetic song": {
        "song": "the examples - synthetic song",
        "artist": "The Examples",
        "artistFans": 1200,
        "songRank": 50000,
        "lookedUpAt": "2026-10-01T12:00:00+00:00",
        "lookupVersion": 1,
        "settled": null
      }
    },
    "marks": {
      "the examples - synthetic song": {
        "song": "the examples - synthetic song",
        "marks": [
          "OddBars"
        ]
      }
    },
    "resolvedNames": {},
    "instructions": {}
  }
}
```

## `GET /api/measuring-exclusions`

The tracks, and stretches of tracks, the maintainer marked as not fit for measuring detectors, for
the harnesses to leave out (ADR 0085). Ordered by `videoId`; a track not listed is not marked.

```json
{
  "tracks": [
    {
      "videoId": "Zx3TrackA01",
      "song": "the examples - synthetic song",
      "wholeTrack": false,
      "reason": null,
      "stretches": [
        { "startSeconds": 364.0, "endSeconds": 380.0, "typed": "183–190", "reason": "Slows down and turns to chaos" }
      ]
    }
  ]
}
```

`wholeTrack` true means leave the whole track out (`reason` says why, `stretches` is empty); false
means leave out each stretch, in seconds of the source file. `typed` is what the maintainer wrote
(Live's bars, or `m:ss` for a track with no warp) and is for display. The same record is in
`GET /api/tracks/{trackKey}` at `records.measuringExclusions`.

## Investigating a track

Start here: `curl` the track, then read `row.state`, `row.next` and `jobs` for where it is and
what happened, `records` for what the stores hold and under which key, and `stems[].warp` for its
warps. Fall back to the files when the app is not running, or when the question is about a file
the API does not read: the log, `source-index.json`, the ledger's history before its latest run.
