# crateside: choosing which beatdetect build reads the tempo, on the Settings page

Issue: #102

For a Claude Design session. The implementing agent ports your design to Blazor, so send layouts
and states, not code.

Built against the commit that adds this brief. The rules as built are in `docs/design/DESIGN.md`,
and your canvases are in `docs/design/canvases/`; Settings is the existing page with the Playlist
section, and the new section sits beside it in the same row-and-control idiom.

---

## Where it stands

crateside reads each instrumental's tempo, beat grid and downbeat with `beatdetect`, an ffmpeg
filter FiLL writes. It comes in **builds**, each an `ffmpeg.exe` named by version and commit
(`1.3.0-1e57fec`), and later builds are generally better. crateside keeps a list of the builds it
knows and **pins one** for beatdetect only; the ffmpeg crateside uses for everything else is
untouched.

A track's reading remembers the build that made it. Pinning another build does **not** re-read the
library: old readings stay, and are marked as from an older build. Only a track that needs a
starter warp is read by the pinned build, and a track's reading can be redone by hand.

## What the user does

- **Sees the known builds**: label (`1.3.0-1e57fec`), where the executable lives, and which one is
  pinned. A build with no commit (a release, or an exe with no `build.json` beside it) shows its
  version alone.
- **Adds a build** by giving the path of an `ffmpeg.exe`. crateside runs it to identify it. Three
  outcomes need a state: added, already known (perhaps with a new path), and *not ffmpeg / cannot
  be run*.
- **Pins a different known build.** One action; the pinned build is the only one that can't be
  removed.
- **Removes a build** that isn't pinned.
- **Sees what the pin means for existing readings**: how many tracks' readings are from the pinned
  build and how many from an older one. This is a figure, not an action.

## Leave room for

- A per-track "read again with the pinned build" action on Track details, and an *older build*
  mark on a track's tempo there. Not part of this page, but the mark needs a shape that fits the
  existing tempo display.
- Builds that crateside builds itself from a ref. Not now.
