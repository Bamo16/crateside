# Handoff 05: Track details (the song page)

Issue: #21

Back-filled on 2026-09-25 from the Design session's comment on #21, which was the only copy.

The design is in the attached zip, at `canvases/Crateside Track Page.dc.html`. Open it in a browser; it needs `support.js` beside it. **Turn 2 (artboards 2a–2c) is on top and supersedes turn 1 where they differ.** Turn 1 (1a–1i) still carries the full Done layout and the other seven states.

It was drawn against `7d6f916`: `Song.razor`, `Evidence.razor(.css)`, `PageHeader`, `BrandNav`, `app.css`. No new tokens; nothing new is coloured.

### Name
- Page: **Track details**. Row link: **Track details** (replaces *Why this key*). Tab title: `{name} · crateside`.
- Route can stay `/song/{VideoId}`. Header shape: **Bar**, with the Tracks tab active.

### Layout, top to bottom (Done, 1a + 2a)
1. `← Tracks`, then the name as an h1 (28px/600) with a meta line in text-60: genre · N uploads.
2. **Verdict band.** A `--surface` box, radius 10, four cells split by `--line-strong`: **Key · Confidence · BPM · Where it is**. The three values are mono at one size (52px): key and BPM at 600, confidence at 500. Under each value is one sub-line in text-60:
   - key: "tagged in Ableton on N of N samples"
   - confidence: "judged {date}"
   - BPM: beatdetect's figure and whether it's a half/double or a conflict
   
   The fourth cell holds the state chip, the "what happens next" sentence, and a "Why {key} ↓" anchor to the evidence. Colour follows state only; the BPM goes amber when the track is ready to warp, as on Tracks.
3. **Stage flow** (2a, 2c). A strip attached under the band (`#17160f`, rounded bottom corners only). It shows the **current pass** as chips joined by short rules, each finished chip with its date and time beneath.
   - Chip states: ✓ done (neutral) · ● running (teal filled, **pulsing**, the only animation on the page) · ● your turn (amber) · ○ not yet · – skipped (dashed, 55% opacity) · ■ stopped (red outline for parked, red filled for gave up, with the reason or strike count in the label).
   - Stages: Download · Detect · Judge · Separate · Stage & tag · Warp.
   - **A pass** is a run from some stage to the end: the first run, a re-cut, a source requested later, an added upload. Stages the pass doesn't need are drawn *skipped*; nothing ever un-completes. The label on the left reads "Pass N · {reason}", and "N earlier passes ↓" jumps to the log.
4. Two columns, `minmax(0,1fr) 19rem`, gap 44px.
   - **Left: Samples.** The same `.stem` lines and grid as the expanded row, with columns Sample · (From, only when there are several uploads) · Where · Ableton key · Audio. After them come the warp-guide line (dashed; "still here" or "deleted"), then a folded one-line "N samples sent to the recycle bin", then the **Change what is cut** opener. The list is the picker here exactly as on the row (#22), locked the same way.
   - **Left: Evidence.** A section with `id="evidence"`, headed **Why {key}** plus a one-line summary. `Evidence.razor`'s content is unchanged. Restyle only:
     - Kind badges go monochrome: *authoritative* filled `--text`, *corroborating* outlined, *algorithmic* and *chords only* dashed. The old green/teal/amber now mean done/working/your turn.
     - Stance glyphs are ✓ in `--text` and ✗ in text-60. Only *relative split* and *kind ≠ host* stay red.
     - Each source row is `16px 112px minmax(0,1fr)`: host, then stated key (mono), then note, **stacked in the third column**. A fixed key column overlapped the host in narrow widths.
     - Key flicker is folded by default. The tempo line stays but is secondary to the band.
   - **Right rail**, top to bottom:
     - **Change**: the row's actions (Rename · Swap source · Add an upload · Change genre · Open folder · Remove from playlist).
     - **Upload(s)**: a click-to-load player per upload, 16:9, showing a still and a play glyph. Nothing is fetched from YouTube until it's pressed, so the page stays local-only. Beneath each: video id, "YouTube ↗", and its role.
     - **Genre**: the settled value, plus what the sources called it. A pending suggestion appears as an amber-outlined box with *Use {genre}* / *Pick another*.
5. **Activity** (2b). The last thing on the page, collapsed to one line: "Activity · N entries across N passes · last {timestamp}", with a **Copy all** button that works while collapsed. Opened, it shows:
   - All / Problems / You filters.
   - A mono log newest-last in a `#0f0e0c` box, scrolling at about 20 lines. Columns: full local timestamp with offset · source (stage name, or **you** in amber) · pass · message. Pass dividers are rows of their own. Failures show in red.
   - The copied text starts with a header block: name, video id, state, verdict, copy time, entry count.

### Actions: decision
**Actions live on both the row and this page.** They must be the row's **own components** mounted a second time, with the same confirms and copy, not a reimplementation. Otherwise the two surfaces drift. Rename opens the name strip above the band. Findings (Same song / Different song, Use it anyway) are answered in a red-outlined strip placed **above** the band (1e).

### States (turn 1)
- **1b Ready to warp:** amber chip; samples in staging; warp guide present; last flow chip amber.
- **1c In flight:** teal chip plus elapsed/expected time and a thin progress bar; Samples becomes "Will be cut into" (the plan).
- **1d Never judged:** the band keeps its shape with dashed empty slots; the state leads; no evidence section at all.
- **1e Parked / needs an answer:** the finding strip sits above the band.
- **1f Library-only:** the key cell reads "Key · tagged in Ableton" in text-75; confidence and BPM show `—`; dashed "library only" chip; no flow, plan or evidence.
- **1g Several uploads:** a From column appears and the rail lists every upload. The header's YouTube link is the primary upload's.
- **1h Judged, no evidence:** the evidence section is one line: "Judged {date}, before crateside kept evidence. The verdict stands; its reasons were not saved."
- **1i Cell variants:** collapsed mode ("really D Dorian, tagged as D minor"), BPM conflict (red "conflict"), LOW (red, key shown with "?"), WITHHELD (red, key `—`).

### Left off
- GPU line position, outside the in-flight state.
- The folder path as a link (*Open folder* is in Change).
- Recycle reasons beyond one line.
- The input kind appears only in the evidence's provenance line.

### Needs something backend
- **Per-stage timestamps** for the flow's dates.
- **A pass record** (start stage, reason, which stages it skips) so the flow can draw skipped stages and "N earlier passes".
- **An activity log** per track with the fields above. If these aren't recorded today, the log is the thing to add first, and the other two fall out of it.
- Genre still has open questions of its own; the rail just shows whatever state the genre is in.

### The zip
`crateside-design.zip` is the whole design archive to date: all three canvases, every handoff sent, the 1a brand assets, and the briefs. Its `README.md` indexes it. **Decide where (and whether) it lives in the repo**, and whether future UI changes should update anything in it. One option: commit it under `docs/design/`, and keep a short living `docs/design/DESIGN.md` with the rules from the zip's README. Update `DESIGN.md` whenever a change touches tokens, chips, header shapes or table caps, so the next design session starts from what's actually built. The canvases and handoffs would stay frozen as history.

[design handoff.zip](https://github.com/user-attachments/files/32668954/design.handoff.zip)
