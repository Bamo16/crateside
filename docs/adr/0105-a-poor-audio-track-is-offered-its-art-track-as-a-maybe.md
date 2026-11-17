# A poor-audio track is offered its Art Track as a maybe

Date: 2026-11-17. Builds on ADR 0019 (probe the source before fetching) and StemForge ADR 0017
(the Art Track lookup).

## Context

A track parked for poor audio (`ParkReason.PremiumShortfall`) has an upload with no premium format.
Many such videos have a twin: YouTube Music's audio-only "Song" upload, the Art Track, which does
carry premium audio. YouTube's own video-to-song link is signed-in only, and crateside never uses
Bamo's cookies or account for this.

## Decision

1. **StemForge looks it up**, because everything that talks to YouTube goes through it.
   `stemforge-cli art-track <link>` prints the song's video id, title, artist, length and the length
   difference, or `{"found":false}`. How it matches is StemForge's ADR 0017.
2. **The poor-audio finding shows it as a maybe**, in `TrackFindings`: a link to the song with its
   title, artist, length and how far that length is from this upload's. The wording says it may not
   be the same recording. Nothing is swapped, queued or downloaded; Bamo opens the link and adds it
   as a source himself if it is right.
3. **One lookup per upload, cached.** `ArtTrackLookup` keeps every real answer in
   `%APPDATA%\crateside\art-tracks.json`. A found song stays; "nothing found" stands for 14 days,
   since YouTube Music may list the song later. A failed run (StemForge missing the command, no
   network, bad output) is not kept, so the next view asks again. Lookups run one at a time, and the
   finding asks only for a track parked for poor audio, so a page of rows does not fan out to YouTube.
4. **It follows the upload in use**: `StatedSourceUrl`, else the track key, as `Use it anyway` does.

## Consequences

- The command exists only in a StemForge build that has it. With an older `stemforge-cli` the run
  fails, no suggestion shows, and nothing is cached; the finding is otherwise unchanged.
- It misses about 4 in 10 songs (title differs, or no song exists), so no suggestion never means
  there is no Art Track.
