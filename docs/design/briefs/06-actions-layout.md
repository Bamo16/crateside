# crateside: where a track's actions live (row and Track details)

Issue: #62

For the Claude Design session that drew the Tracks page, the brand and Track details. This brief
covers **one component set shown in two places**. The implementing agent ports your design to
Blazor afterwards, so send layouts and states, not code.

Built against `3ecb2e7`. The canvases are in `docs/design/canvases/`, and the rules as built are in
`docs/design/DESIGN.md`.

---

## The problem, in the user's words (2026-09-25)

After seeing Track details built from your turn 2:

> location of open folder/swap source buttons and their descriptions is very janky and confusing
> (word wrap issues). Note that I'm not sure I'm really happy with the locations of the buttons in
> the expanded track rows either (including track details button).

The rail's word-wrap was patched: the rename and swap strips now stack when narrow. The layout
question behind it was not. Your turn 2 decided that **actions live on both the row and Track
details, as the same components mounted twice**. That is now true in the code, and it stays a
constraint here. The components are:

| component | what it holds |
|---|---|
| `TrackFindings` | a finding that needs an answer: *Same song / Different song* (#41), *Use it anyway* (poor audio) |
| `GenreStrip` | the genre choice: seven genres + *none of these*, a note of what the sources said, or *from your answers for {artist}* |
| `FamiliarityStrip` | *Mainstream / Obscure*, with a note: *{artist} 14,000,000 fans · song rank 900,317 · from these numbers* or *your call* |
| `TrackEdits` | two status lines, each with its button. **Name**: *named from the file names* · **Rename** (· **Revert**), or *rename it in Ableton* when a sample is already in the library. **Source**: *cut from the playlist entry's own video* · **Swap source**. Each opens an inline editor (text box + a sentence + confirm/cancel) |
| `StemList` | the samples and the picker (#22): *Change what is cut*, *Add a source*, *Adopt a file* |
| `StemConfirm` | the picker's confirm, shown only once a tick has moved |
| `TrackActions` | buttons: **Open folder** · **Track details** (row only) · **Retry** (gave up only) · **Cut what is missing** (short only) · **Work this first** / **Stop working this first** · **Remove from playlist**, or **Remove** with a confirm for a track no longer in the playlist |

## Where they sit today

**Expanded row** (`Tracks`): a two-column grid, `1fr 340px`.

```
[ finding strip, full width, only when there is one ]
[ Genre: Drum & Bass  Future Bass  Hip Hop …  none of these   heard: … ]      (full width)
[ Familiarity: Mainstream  Obscure   1991 4,944 fans · song rank 317,277 ]    (full width)
[ named from the file names · Rename ] [ cut from the playlist entry's own video · Swap source ]
[ Stems list + picker                  ] [ side: what happens next (sentence)          ]
[                                      ] [       detail sentence, retry note           ]
[                                      ] [       recycled files                        ]
[                                      ] [       Open folder · Track details · Remove… ]
```

**Track details** (`/song/{id}`): left column is samples, then evidence, then activity. The right rail
(19rem) holds, top to bottom: **Change** (`TrackActions`, then `TrackEdits` stacked), the upload
players, `GenreStrip`, `FamiliarityStrip`. The confirm of a picker change also appears in the rail.

## What seems wrong (for you to confirm or overrule)

- **Status and action are split.** *Named from the file names* is a status; **Rename** is an act on
  it. Today they share a line, which wraps badly when narrow and reads as a sentence with a button
  stuck in it.
- **The action buttons are a flat, unranked row.** *Open folder* (harmless, frequent), *Remove*
  (destructive), *Work this first* (queue control) and *Track details* (navigation) all look alike and
  sit together.
- **Three choice strips stack at the top of the row** (genre, familiarity, and the name/source
  pair). They push the stems, the thing most often looked at, down.
- **Track details' rail has no order of importance.** Change comes first, before the uploads and
  the tags.

## What to design

1. **An order and grouping for these actions** that works in both the wide row panel and the 19rem
   rail. The same components are used, so a layout has to reflow, not fork. Group by kind of act:
   navigate, tag, change what is cut, queue, remove.
2. **The status + action pattern** (name, source): how a status line offers its one action without
   wrapping into nonsense.
3. **Where tags go** (genre, familiarity): together as one "tags" block, and whether a settled tag
   collapses to a single line (*Drum & Bass · change*) instead of showing every option.
4. **Destructive actions** (*Remove*, *Swap source*) set apart from the everyday ones.
5. States to show: a row with a finding waiting; a ready-to-warp row; a library-only row (no
   rename, no swap); a row with a rename open; Track details' rail with the picker confirm open.

## Constraints

- No new tokens and nothing newly colored (DESIGN.md). Amber is still only *your turn*, red only
  *needs attention*.
- Buttons are the existing `.outline` / `.primary` / `.danger`, global in `app.css`.
- Every component above must stay one component mounted twice. Say what differs by context (the
  row hides *Track details*; the rail stacks), and the implementer passes it as a parameter.

## What to send back

As last time: a canvas with the states, and a written handoff. Post them as a comment on the issue
with the files attached. The implementing agent builds from that comment.
