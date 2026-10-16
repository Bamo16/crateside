<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="src/Crateside/wwwroot/brand/lockup.svg" width="320" height="54">
    <img src="src/Crateside/wwwroot/brand/mark-tile.svg" alt="crateside" width="72" height="72">
  </picture>
</p>

# crateside

**A bench for building a mashup sample library in Ableton Live.**

You keep a YouTube playlist of songs you want to mash. crateside turns each one into
key-tagged vocal and instrumental samples in Ableton's browser, and stops exactly where a
human has to take over — warping — telling you, in one list, what is waiting on you and
what the machine is still doing.

It is a Blazor Server app that runs on your own PC, meant to sit on a second monitor
next to Ableton. There is no cloud, no account, and nothing to deploy.

## What it does with a song

For every track in the playlist, in order, crateside:

1. **Downloads** the audio (via [StemForge](https://github.com/Bamo16/StemForge)'s
   `stemforge-cli`), and notes whether it got YouTube Premium quality.
2. **Detects the key** with several detectors, then has a Claude agent **judge** the
   evidence into a single verdict with a confidence — HIGH, MEDIUM, LOW, or withheld.
3. **Separates** the track into stems on the GPU: by default a full vocal, a full
   instrumental, and a drums stem that exists only as a **warp guide** for placing
   markers and is meant to be deleted afterwards.
4. **Stages** the stems into your Staging folder and **writes the key tag**
   Ableton's browser reads.

Then it is your turn: warp the samples in Ableton, set their default clips, and drag them
into the library. That move is the only "done" signal crateside looks for — nothing
is marked finished by hand.

Only one of those stages costs anything scarce (judging spends Claude quota; separation
occupies the GPU), so each runs at its own moment and the whole queue **drains**
continuously, one track at a time, until nothing is outstanding.

## The pages

Every page carries the same bar, so the tabs never move between them. Tracks' is taller
and stays stuck to the top of the window: it also shows what is separating, what is
judging, and how much of the current Claude quota window is used.

<!-- SCREENSHOT: Tracks page, default filter, one row expanded. -->

- **Tracks** — one row per song. A row moves only on reaching _ready to warp_, being
  parked, or being finished with — never on an ordinary stage change, so the thing you
  were looking at stays where you left it. Filters: _Unfinished_, _In staging_ (ready to
  warp), _In flight_, _Needs attention_, _All_. Expanding a row shows its files, where
  each one is, the verdict and why, and the controls for that track: rename it, swap the
  upload its stems are cut from, change which stems are cut (a **re-cut**, with a
  confirmation that lists exactly what will be deleted), or remove it from the playlist.
- **Song** — one page per track, the long form of an expanded row.
- **Library** — how many samples you have in each key, across everything already in
  the library. Counted from Ableton's own index, which is what the browser shows,
  so it reaches files the folder's tag store has never caught up with.
- **Partners** — pick a key and get the keys that pair with it, ranked by how cheap
  they are to try (pitch shift first, circle-of-fifths distance second). It returns
  keys, not files, because the filtering happens in Ableton's own browser.
- **Settings** — paths, pause/resume, the YouTube connection.

## Things it deliberately does not do

- **It never guesses that a file is warped.** An `.asd` sidecar means Ableton analysed
  a file, not that you warped it, so crateside does not read them. Presence in
  the library is the signal ([ADR 0002](docs/adr/0002-there-is-no-warped-signal.md),
  [ADR 0007](docs/adr/0007-the-move-out-of-staging-is-the-warp-signal.md)).
- **It never writes into Ableton's own database.** Tags go into the
  `Ableton Folder Info\*.xmp` store for Staging only; everything else is read
  from Ableton's index, which is what the browser actually shows
  ([ADR 0003](docs/adr/0003-crateside-may-write-abletons-tag-store.md),
  [ADR 0013](docs/adr/0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)).
- **It does not stop to ask.** When it cannot proceed — no key worth tagging with, or a
  download that missed Premium audio because of the upload itself — the track is
  **parked** and shows up under _Needs attention_ with the reason and the one action that
  clears it. Parking is a finding you resolve, not a flag you clear.
- **There is no "hold".** The playlist is the inbox; the way to stop a track is to
  remove it from the playlist ([ADR 0010](docs/adr/0010-the-playlist-is-the-inbox-not-the-work-list.md),
  [ADR 0015](docs/adr/0015-hold-is-removed-and-the-playlist-is-the-way-out.md)).
- **A song is identified by the ledger's song, not by a name or a URL.** One song can
  have been cut from several uploads — that is what swapping a source leaves behind — so
  the row is keyed by the song and its files say which upload each came from. Renaming a
  track renames its files and tags; swapping its source changes what the stems are cut
  from; neither changes what the song _is_
  ([ADR 0012](docs/adr/0012-the-source-url-is-an-instruction-not-an-identity.md),
  [ADR 0014](docs/adr/0014-the-song-is-the-identity-and-the-name-is-not.md)).

## How it relates to keytag and StemForge

- [**keytag**](../keytag) (private) is the key-detection tool. crateside keeps the
  **ledger** of every verdict itself, in SQLite ([docs/ledger.md](docs/ledger.md)); until
  the judge writes there directly (#111), crateside imports the verdicts keytag's recorder
  appends to its `results/runs.jsonl`. keytag never learns crateside exists.
- [**StemForge**](https://github.com/Bamo16/StemForge) does the downloading and the stem
  separation. crateside calls its CLI and reads the provenance it writes into each file's
  tags — that provenance (the video ID) is how every later step recognises a file.

Beyond the ledger, crateside keeps very little: the YouTube OAuth token, a record of the work it has
run, and per-track instructions (the plan for which stems to cut, a name override, a
chosen source). Everything else on screen is derived from the ledger and the Ableton
folders, so nothing falls out of sync.

## Running it

Requirements: Windows, .NET 10 SDK, Ableton Live with a Staging folder and
a library folder as watched roots (both set in Settings), keytag and StemForge checked out as siblings,
a GPU for separation, and a Claude subscription for judging.

```
dotnet run --project src/Crateside          # the app
dotnet watch --project src/Crateside run    # UI work only — see below
dotnet test --solution Crateside.slnx       # MTP; a positional solution path is an error
dotnet csharpier format .                   # before every commit
```

`watch` hot-reloads by patching the running process, which it cannot do for a change that
reshapes an object already in memory — so a multi-file edit can leave the app running code
that is genuinely inconsistent while reporting *"Hot reload succeeded"*. Use it for razor and
CSS; use `run` for anything else.

Many tests assert against the real Ableton roots and the keytag ledger and skip when
those are absent — that has repeatedly caught things fixtures would not have.

## Reading further

- [CONTEXT.md](CONTEXT.md) — the glossary. Every noun above (track, stem, sample, warp
  guide, verdict, parked, recipe, work order) is defined there or in keytag's.
- [docs/adr](docs/adr) — seventeen decision records; the ones linked above are the
  load-bearing ones.
- [docs/the-manual-workflow.md](docs/the-manual-workflow.md) — the human half, step
  by step, for what to do once a track is ready to warp.

<!-- LOGO: small mark, 24px, could sit before the footer line or be omitted. -->
