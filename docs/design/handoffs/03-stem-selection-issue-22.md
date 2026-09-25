# Handoff: stem selection, additional uploads, adding tracks — closes the design half of #22

## Overview
Turn 3 of the Tracks page design. The design canvas is `Crateside Unified Page.dc.html`; **turn 3 (artboards 3a–3e, top of the canvas)** is this spec. Turns 1–2 are the built page and are reference only.

Read against the repo: `Tracks.razor.css`, `TrackRow.cs`, `StemRecut.cs`, `WorkOrder.cs`, ADRs 0004/0005/0014/0015. Every correction in #22 is carried (no hold, no Done filter, song-keyed rows, parked reasons, side-by-side edits row, `--table-tracks` cap). Tokens are the ones in `app.css`; class names below refer to `Tracks.razor.css`.

## The decision
**The stem list is the picker.** There is no separate control. Every file the track could have is a `.stem` line; a leading 22px column holds a tick. A line is either on disk or not; the tick says whether it is wanted. Toggling recolours the same lines into the existing diff classes (`.coming` teal *new*, `.going` red struck *removed*, plain *kept*). The confirm state is therefore the list itself plus the right-hand `.side` column — no second screen.

The `has vocals / no vocals` select (`.release`) is **removed, not replaced**. An instrumental release is "Vocal - Full unticked, Source ticked". See notes 1.

Names shown are the separator's (`Vocal - Full`, `Instrumental - Clean`) — the filenames Ableton shows. crateside's preset ids appear nowhere in the UI; the plan panel loses its mono argument list.

## 3a — after separation (primary case)

### Reading (default)
Panel as built: edits row (name strip · cut-from strip with **Swap source**), `.panel-head` **FILES · SAMPLE STAGING / {name}** (was STEMS), `.stem-heads` Sample · Ableton key · Where it is, one `.stem` per file on disk, `.stem.guide` for Drums. The `.release` row is replaced by an **opener**:

- Full-width button, `display:flex; gap:10px; margin-top:8px; padding:8px 12px`, 12px/500 text-75, 1px **dashed** `rgba(255,255,255,.18)`, radius 6; hover border `.4`, text `--text`.
- Content: mono `+` · `Change what is cut` · right-aligned hint text-50 (ellipsis): "3 more presets, the source track, other uploads".

### Picking
Opener expands in place; the list becomes the full inventory. Grid `22px 1fr 120px 140px 1fr`. Band labels between groups: mono 10.5px/500 text-35, padding `8px 12px 4px`: **VOCAL**, **INSTRUMENTAL**, **WHOLE TRACK**, **OTHER UPLOADS OF THIS SONG**.

Lines and their tick:
- On disk, ticked (kept): as built `.stem`; tick = 14px square, radius 3, bg `--text`, ink `✓` mono 10px/600. Note cell reads `kept` text-50.
- Not on disk, unticked: border `rgba(255,255,255,.08)`, no bg, opacity .6; empty 14px box 1px `rgba(255,255,255,.3)`. Ableton key `—` text-30, Where `not made` text-50.
- Ticked but not on disk (**new**): `.stem.coming`; tick bg `--teal`; Where `new` teal; first new line notes *tagged from the existing verdict*.
- On disk, unticked (**removed**): `.stem.going`; empty box border `rgba(224,122,109,.6)`; Where `removed` red; note *deleted from Sample Staging, with its .asd*.
- **Source** line (WHOLE TRACK): label `{name} (Source)` + `.kind` "source track". Where `not kept` / `Sample Staging`. Note *copied, not separated · costs nothing*.
- **Drums** line: `.stem.guide` as built, but the tick cell is a mono `●` text-50, **not a box** — pinned, not a choice. Note *always made · delete after warping*; when other files survive a re-cut, *kept — no new guide is cut*.
- **OTHER UPLOADS** band: added uploads as lines (see 3c) and an **Add an upload** button in the opener style.

Inventory today: Vocal Full/Clean/Balanced, Instrumental Full/Clean/Balanced, Source, Drums. Drive it from the separator's preset list, not a hard-coded six.

Right column (`.side`) while picking — replaces the action row:
- Additive (nothing removed): lead sentence **teal** 13px "2 more files. Nothing removed."; `.detail`: passes + minutes (`SeparationRecipe.Estimate` shape: 95 s per pass), key stays, no quota, audio still on disk (`RecutPlan.AudioDetail`), "Files already in Sample Staging are untouched."; buttons `.primary` **Cut N more** + `.outline` Cancel.
- Mixed / removing: `.panel-head` gains ` · re-cut pending` in `.pending`; lead sentence `.warning` red "Removes N files from Sample Staging. N new, N kept."; `.detail` names the removed files' lost Ableton work; `.danger` **Cut N · remove N** + Cancel.
- Unticking every file on disk = the old whole-set re-cut, same lines, button "Cut N · remove all N".
- Only removal turns the button red.

### Locked (any file in Mashup Samples)
Lines still read; ticks become 14px boxes with 1px **dashed** `rgba(255,255,255,.3)`, ✓ in text-50; opener is inert (`.locked` treatment) with hint text-60: "N file(s) are in Mashup Samples. Move them back to Sample Staging to change this."

## 3b — before separation
Same list, always open — it **is** the body (`.panel-head` **WILL BE CUT INTO**, hint text-50 "changes apply now — nothing has been made yet"). Grid `22px 1fr 1fr` (no key / where columns). Ticks apply immediately; no confirm. Default for a new track: Vocal - Full + Instrumental - Full ticked, Source off. Right column: "N files. N pass(es) on the GPU, about N minutes." + detail + actions (Why this key · Bump to top · Remove from playlist). Editable while `TrackRow.CanChangePlan`.

## 3c — additional uploads
Inside the OTHER UPLOADS band. **Add an upload** → inline form: 1px `rgba(255,255,255,.4)` box, grid `1fr 220px`: URL input (mono 13px) · **label** input (required). Preview line text-50: `becomes {name} ({label}).flac · tagged {key} · downloaded, never separated · the label is required and is the filename suffix`. Right column: teal lead "Adds 1 file. Removes nothing."; detail: restated in keytag as this song, no quota, no GPU, lands in Sample Staging, takes the track's key; **"To replace the upload the stems are cut from, use Swap source above instead."**; `.primary` **Add upload** + Cancel.

Afterwards: one line per upload in the band, same `.stem` grid, label `{name} ({label})` + `.upload .mono` video id (ADR 0014), note *whole upload · added by you*. While downloading: `.coming` tint, Where cell teal dot + `downloading`. **Unticking an added upload is its removal path** (goes through the same red confirm). Collapsed row while adding: name-cell flag `● adding upload` (teal, renaming's slot); chip untouched.

## 3d — adding tracks without the playlist
Filter bar gains **+ Add track** (pill, 1px `rgba(255,255,255,.4)`) right of search. Popover 520px, `--raised`, radius 10, shadow, segmented **YouTube URL | Local file**.
- URL tab: mono input; line text-60 "Will be called *{resolved name}* · runs the whole workflow: detect, judge, separate, stage, tag. Not added to the playlist — crateside keeps its own record of it."; `.primary` **Add track**.
- Local file tab: dashed drop zone (.flac/.wav); name input ("the name — nothing else can supply one"); a red `.finding`-style strip **No identity yet.** + explanation; Add track.
- Rows: pasted URL → ordinary row, `.flag.neutral` **added by URL**, no `PlaylistItemId`. Local file → `.flag.neutral` **local file** + red flag **no identity**, chip `parked · no identity` (a third `ParkReason`). For both, *Remove from playlist* becomes **Remove** (drops crateside's own record).
- **Do not ship the Local file tab until notes 3 is decided.** Ship the URL tab alone.

## 3e — notes (decide before or while building)
1. **The plan is now the record — ADR 0004 reversed on purpose.** The picker stores the plan per track in the instruction store; nothing derives it. Cost: `has_vocals` stops flowing to keytag (ADR 0005 §6–7). Options: drop the field and amend keytag's glossary, or infer `has_vocals:false` when no vocal preset is ticked *and* the source is kept — labelled as an inference in the ledger, not a statement.
2. **Warp guide is not a choice.** Pinned line, dot not box. If drums becomes a preset upstream, the line stays pinned. A re-cut that keeps any file keeps the guide; a new one is cut only when nothing survives.
3. **Local-file identity is unsolved.** Every reader joins on StemForge `COMMENT` provenance. Two ways out, neither decided: crateside writes provenance into audio (against ADR 0003's one-write rule) or keytag mints an identity. Until then: `parked · no identity`.
4. **Added upload vs done.** Recommendation: adding a source **reopens** the row to `ready to warp` with one outstanding file. Rows may move on reaching ready to warp, so this is permitted.
5. **Additive re-cut needs machinery.** `StemRecut.Apply` deletes every staged file and cancels three stages. "Cut N more" needs a recipe of only the new presets and a handoff that tolerates existing names for the same song (today a collision fails the track — ADR 0005 handoff). Build it or change the sentence "Files already in Sample Staging are untouched."
6. **No warning for never-used presets.** Cost line (passes, minutes) is the only thing worth saying.
7. **Order survives.** Re-cut is a stage change → row stays put; moves back into the ready group when it lands there again. Added uploads never touch the chip.
8. **Pasted-URL record.** The store that lets a non-playlist track reach `Outstanding` must carry provenance, or the *added by URL* flag cannot be drawn.

## Acceptance mapping (#22)
- Picker in both bodies, same control, post-separation primary → 3a, 3b
- One vocabulary, dropdown gone → 3a/3b lines, notes 1
- Confirm diff: nothing removed / mixed → 3a additive, 3a mixed
- Add/remove uploads, visibly not a swap → 3c
- Add by URL and local file drawn → 3d
- Notes carry reversal + `has_vocals`, local identity, done reading, warp guide → 3e 1–4
- Default order, locked state, warp-guide rule survive → 3e 7, 3a locked, 3e 2

## Files
- `Crateside Unified Page.dc.html` + `support.js` — canvas; turn 3 (3a–3e) is this spec.
