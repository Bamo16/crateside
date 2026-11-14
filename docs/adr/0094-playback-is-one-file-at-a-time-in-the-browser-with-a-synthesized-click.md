# 0094 - Playback is one file at a time in the browser, with a synthesized click

- Status: Accepted
- Date: 2026-11-11
- The first half of [#105](https://github.com/Bamo16/crateside/issues/105); the layout is
  [brief 22](../design/briefs/22-playback.md). Warp editing (moving the marker, setting the clip
  start, saving the default clip) stays on #105 for a later brief built on this.
- Closes the question parked under [ADR 0072](0072-bars-are-numbered-as-lives-ruler-numbers-them.md):
  there is no "the clip start is bar N" setting.

## Context

#105 asks for the last two adjustments to a starter warp to be made in crateside instead of
Live, and for that it needs playback with a click on the warp's grid, accurate to about ±5 ms,
because the grid is judged by ear. Track details already draws the warp view (a stem per lane, a
ruler in Live's bars, a key strip), and handoff 20 left room for a transport and a playhead. The
side stack embeds YouTube's player for each upload.

What there is to play, **measured 2026-11-11**: the source index lists 699 sources, all FLAC and
all on disk (566 in working sets, 97 kept samples, 36 imports). A working-set source is discarded
once retention lapses (ADR 0061); the stems in the library stay.

## Decision

- **Playback runs in the browser, one file at a time.** The page fetches the chosen file, decodes
  it with Web Audio and plays it from memory: the mix (the source) or any one stem. Switching keeps
  the position. Several stems at once was declined: decoding them all costs several hundred MB, and
  mixing them on the server invites drift; mute and solo can follow if they are missed.
- **The click shares the audio's clock.** It is scheduled in the same `AudioContext` as the file, on
  the beats of the warp the warp view shows, through every marker, with an accent on the first beat
  of each bar (bar length from the warp's time signature, as ADR 0072 reads it). Both therefore
  reach the ear together whatever the output latency; the playhead on screen is drawn behind the
  context's reported output latency. With no warp there is no click.
- **The click sounds are synthesized**, three of them named as Live names its own (Classic, Click,
  Wood), each with an accent voice, tuned by the maintainer's ear against Live's. Live's samples
  are Ableton's and cannot ship in a public repository, and reading them from an installed Live
  was rejected as clunky; CC0 recordings stay a fallback if a synthesized sound never comes close.
- **Playback state lives in the browser.** The playhead, seeking, the view's window, loop and
  follow run in the page's JS module and tell the server only what it must keep; a 60 fps playhead
  cannot round-trip over Interactive Server.
- **Audio is served by a localhost endpoint that names a track and a file**, never a path. It
  resolves through the source index and the library scan as they stand at the request, so a file
  Live has moved between Staging and the library is found where it now is. Playing audio is already
  in memory and does not notice a move; a load that finds the file gone says so and nothing retries
  on its own.
- **The picker offers only what is on disk.** Nothing streams from YouTube, and stems are never
  summed into a stand-in for the mix, since separated stems do not add back to the original exactly.
- **A mix fetched again for playback** (*Get the mix again*) is an ordinary `stemforge-cli` download
  into the working set under retention, with its clock started at the download, and **never kept
  less than a day**, so a retention of 0 days does not discard it within ten minutes of arriving.
  The floor applies to ad-hoc fetches (this and *Download the missing ones*), never to a pipeline
  download.
- **Keeping the mix for good is a stem plan change**, not a retention exemption: *Keep in the
  library* makes the separation source a keeper in the track's stem plan and asks for the re-cut
  that copies it, in one action (a plan change alone only states a want, ADR 0027). The janitor
  already discards the working-set copy once the library holds the source verbatim, so no new state
  is stored.
- **One bar numbering, ADR 0072's.** The player's readout gives the bar in it (`bar 34.2.1`). The
  maintainer's sets place each clip start at a verse locator (bar 65, but 73 and 83 on some tracks);
  that numbering belongs to the set, and the harness repositories that use it say so where they do.
- **No Blazor component library.** crateside's look is its own (`docs/design/DESIGN.md`), and the
  libraries bring theirs; for playback the hard part is Web Audio timing, which none of them
  addresses. A later need is judged on its own.
- **The YouTube embed goes**; each upload keeps its ID and its *YouTube ↗* link, which also moves to
  the track's header. Two players on one page could play over each other, and the link still serves
  when the source is gone or the video is the point.

## Consequences

- Only Track details plays. Leaving the page stops playback; the Tracks rows get no play button.
- A track before separation can be heard (the mix), and so can a library-only track (its stems).
- Decoding a whole file costs about 85 MB of browser memory for a four-minute stereo track and a
  short wait before the first play; switching stems decodes again.
- Whether the re-cut that only copies a kept source waits in the GPU queue is unmeasured:
  `SeparationRecipe.For` asks for no preset and no guide for it when the track has a guide, but the
  build checks the queue before the button says "no GPU".
- The chord lane handoff 20 placed under the key strip is not part of playback: the ledger keeps
  chord names without times ([#198](https://github.com/Bamo16/crateside/issues/198)).
