# Handoff 10: a pass on Settings

Issue: none yet, from the user's ask in the design session · built against `e6c3802`
Canvas: `Crateside Settings Page.dc.html` (1a everyday, 1b first run, 1c removal confirm and
failures).

The canvas is a design reference in HTML. Port it to `Pages/Settings.razor` and its `.razor.css`.
No new tokens and nothing newly colored. Buttons are `.outline` / `.primary` / `.danger`, and
choices are `.pill` chips. The header stays `Flush`. The playlist ID, channel name, counts and
times on the canvas are illustrative.

The user accepted the two proposed additions: **Pause separation / judging** and **Folders**.

## 1. Why

The page read like an essay with controls tucked between its paragraphs. *Removing from the
playlist* mixed a switch, an account, a destructive action and a failure list in one run of prose.

## 2. The row

Every setting is one row, `grid-template-columns: 260px minmax(0,1fr); gap: 24px; padding: 16px 0`,
with a `--line` bottom rule. Body cap 944px (replace the 720px), `padding: 28px 48px 48px`.

- **Left**: the name (14px/500), then a one-line gist (12.5px/1.5, `--text-60`).
- **Right**: the control, then a status line (12.5px, `--text-60`). Numbers are mono.
- Section heads are `h3` 16px/600 with 6px below, replacing the 11px caps `h2`. Sections are
  40px apart.
- Each long paragraph from today is cut to one sentence. The quota arithmetic becomes a
  number: "288 of 10,000 quota units a day."

## 3. Choices are pills

Checkboxes and the refresh `<select>` become a row of `.pill` chips, gap 6px. The chosen one is
`.pill.on.plain` and the rest `.pill.off`. A click saves at once, as today.

- Look for new tracks: **Never · 1 min · 5 min · 15 min · 30 min · 1 hour** (the existing
  `RefreshChoices`).
- Library backfill, and emptying the playlist as tracks are taken on: **On · Off**.
- Separation and Judging: **Running · Paused**.

## 4. Sections and rows (1a)

### Playlist
- **Playlist**: the input and **Save** (`.primary`, disabled until it parses and differs), then
  *Open on YouTube*, or the parse problem in red.
- **Look for new tracks**: pills. Status: "{n} of 10,000 quota units a day. The stages still
  read the queue every two minutes on their own." Compute n as `1440 / minutes`; with Never,
  the status reads "Only when you ask."
- **YouTube access**: two lines, each a caps label (`Read`, `Remove`, 11px `--text-40`, 80px),
  then its state, with the button right-aligned:
  - Read: *API key set*, or *No API key* in red plus the command (1b).
  - Remove: *Authorized as {channel}* + **Forget access**, or **Grant access to remove items**
    (`.primary`, then *Waiting for consent…*), or *Not set up* plus a dashed opener *How to set
    up removal*, which unfolds the two `user-secrets` commands.
  - Status: "Removal only works if that channel owns the playlist. The token lapses about
    weekly; reading keeps working."
  - This replaces the separate *API key* section.

### Work
- **Separation** (new): Running · Paused. Gist "Cutting stems. Nothing new starts while paused."
  Status: "Running. Kept across restarts.", or "Paused since {time}. Kept across restarts."
- **Judging** (new): Running · Paused. Gist "Checking keys with Claude. The library backfill
  runs either way." Same status shape.
  - Both pauses are already stored (`PauseStore.RememberSeparation` / `RememberJudging`,
    `paused.json`), but no page calls them. Wire them to whatever the loop reads. If a pause
    control exists somewhere the design session could not find, drop these rows, not the other one.
- **Library backfill**: On · Off. Gist "Judges hand-tagged library tracks, one per idle Claude
  window (0% for 15 minutes), then 5 hours off." Under the pills, the counts in one wrapping
  row: *{n} waiting · {n} judged · {n} gave up, see each row* (red, only when > 0) · *{n} not
  queued · queue them on Counts* (a link to handoff 08's action band; no longer red). Then the
  `NextWindow` line.

### Emptying the playlist
- **As tracks are taken on**: On · Off. Gist "An entry is a message: once crateside has recorded
  it, it can go." Status: "Turning it on removes every recorded entry on the next scan, not only
  new ones. 50 quota units each, tried once, and not undoable from here."
- **Processed tracks**: "{n} in the playlist are done" with **Remove processed tracks**
  (`.outline`, right) and the status "Their stems stay exactly where they are." Drawn only when
  removal is authorized.

### Folders (new, read-only)
- One row each: **Sample Staging** ("Where new stems land."), **Mashup Samples** ("The library
  that Counts counts."), **keytag ledger** ("Detector runs, for keys and BPM."). The path is
  mono 12.5px `--text-75` and wraps anywhere. The button is **Open** for the folders and **Show in
  folder** for the ledger (`.outline`).
- Under them: "Read-only. Set in `appsettings.json`; crateside never writes there."
- The values come from `CratesidePaths`.

## 5. First run (1b)

- The playlist input shows its placeholder. Save stays disabled.
- Rows that need a playlist (the interval, emptying) are drawn but disabled until one is saved.
- A missing API key is red (*needs attention*), because everything depends on reading.
  Removal setup stays folded, because it is optional.

## 6. Removing (1c)

- **Remove processed tracks** opens a confirm below itself, in a `--surface` box (radius 8px),
  as Remove does in the side stack: "Remove {n} entries? {n × 50} quota units. Re-checked at
  the moment of removal, not from this page." It holds **Yes, remove them** (`.danger`) and
  **Cancel** (`.outline`). While it is open, the button above goes to disabled opacity.
- After a run, the status reads "Removed {k} of {n}."
- **Left in the playlist** is its own row, drawn only when there are any. The name is red. Each
  line shows the title (13.5px/500) with the reason under it (`--text-60`), and **Try again**
  (`.outline`) only where `CanSucceedLater`.
- Errors that are not per-entry (consent failed, queue unavailable) show as a red status line
  in the row that caused them.

## DESIGN.md

After building, add a short **Settings** section: the row, pills for choices, the one-sentence
rule, and the Folders row as read-only.
