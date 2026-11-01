# The API

crateside serves what its pages show as read-only JSON, so a script or an agent can ask what state a
track is in without reading `ledger.db`, `jobs.jsonl`, the stores under `%APPDATA%\crateside` and
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

## `GET /api/tracks`

The Tracks page's rows, narrowed by the page's own filters:

| query | means |
|---|---|
| `filter` | `Unfinished` (when not given), `InStaging`, `InFlight`, `NeedsAttention`, `TagsToAnswer`, `TempoDrifts`, `OffItsTempo` or `All`, spelled exactly so; any other value is a 400 |
| `search` | a name, key or video ID; like the page's search, it looks through every track and ignores `filter` and `familiarity` |
| `familiarity` | only rows whose familiarity is tagged as this |

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

## `GET /api/tracks/{videoId}`

Everything crateside knows about one track, or a 404 with a problem body when it has neither a row
nor a verdict for the video ID:

- **`row`**: the track's row, the same object `/api/tracks` returns. Null for a song the ledger
  judged that has no row.
- **`evidence`**: the evidence behind the latest verdict: its sections, sources, windows, candidates
  and every detector's readings. Track details' *Why* section draws it.
- **`jobs`**: every line of `jobs.jsonl` for any of the track's uploads, oldest first. A stage that
  finished well says what it did in `summary` ("D major, HIGH, 2 sections; 3 turns, 150 s"); lines
  written before summaries existed, and every line that is not a success, have none.
- **`stems`**: each sample on disk, with the keywords and Collections Live holds for it (`tags`,
  null when it holds none) and its parsed sidecar (`warp`): whose warp it is (`owner.kind`), the
  markers in seconds and beats, the tempo read bar by bar, the default clip and loop in seconds,
  and whether Warp is on. Beside that, crateside's own records about the warp: the one it wrote that
  the sidecar still holds (`written`), the one it last wrote even if saved over (`lastWritten`),
  a retype to a truer tempo (`retyped`), and the copies of the sidecar it kept (`backups`).
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
    "sources": [],
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
          "isUnwarped": false
        },
        "lastWritten": {
          "from": null,
          "length": 31180,
          "written": "2026-10-01T12:58:00Z",
          "isUnwarped": false
        },
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

## Investigating a track

Start here: `curl` the track, then read `row.state`, `row.next` and `jobs` for where it is and
what happened, `records` for what the stores hold and under which key, and `stems[].warp` for its
warps. Fall back to the files when the app is not running, or when the question is about a file
the API does not read: the log, `source-index.json`, the ledger's history before its latest run.
