# 0022 — an adopted sample is a file crateside did not make

- Status: Accepted
- Date: 2026-09-19
- Makes good on the local files [ADR 0019](0019-a-track-has-many-sources-and-one-of-them-is-the-separation-source.md)
  said were coming.
- Amends [ADR 0003](0003-crateside-may-write-abletons-tag-store.md): crateside now writes one
  audio tag. Supersedes nothing.

## Context

The user built a file crateside cannot make. StemForge's instrumental for *Fox Stevenson — Bring
A Coat* dropped guitar and piano they wanted to keep, so they took the original audio for the
long stretches with no vocals, took the instrumental stem everywhere else, lifted the stem by
0.81 dB to match levels, and exported the result from Ableton. It is a better instrumental than
anything the separator offers for that song, and they warped it.

It landed in `Sample Staging` as an ordinary file with no tags at all, because Ableton's export
writes none. To crateside it did not exist. Worse, it was actively harmful: three real-data tests
that assert every staged file yields a source video ID went red, correctly, because one did not.

Every existing way in assumes an upload. An **added source** is another YouTube video of the same
song, identified by its video ID and fetched. A file made on this machine has no video ID and
nothing to fetch, so it fits none of it.

## Decision

**An adopted sample is a file of a track's audio that crateside took in rather than made.** It is
a sample like any other — staged, tagged, warped, moved — and differs in exactly one way that
matters: **nothing can make it again.**

That single fact settles the rest.

- **It is identified by a label, not an upload.** The label is the user's word for it and the
  file's parenthesised suffix. An added source is identified by its video ID because it came from
  one; an adopted sample came from nowhere, so the label is all there is.
- **It is never outstanding.** A plan can ask for a preset that is missing and separation will
  make it. A plan naming an adopted sample whose file has gone is asking for something no stage
  can supply, so it is never work.
- **It is never superseded by accident.** A re-cut removes it only when the plan stops naming its
  label. The fallback that governs a kept source — drop it unless `KeepSource` — would have
  deleted a file the user spent an evening building, because it reads as a source carrying the
  track's own video ID.
- **Dropping it is deletion, and that is the whole of it.** There is no re-fetch and no re-cut to
  undo the mistake, so the confirm has to say so.

### crateside writes one audio tag

For any reader to join the file to a track it must carry provenance, and nothing else will write
it. So crateside writes the `COMMENT` tag itself, once, at adoption:

```
crateside/<version> | label: <label> | date: <yyyy-MM-dd> | source: https://www.youtube.com/watch?v=<videoId>
```

**The prefix is `crateside/`, not `stemforge/`.** Impersonating StemForge would have cost no
parser change, and was rejected: the tag is the only record of where a file came from, and a
reader that cannot tell a separated stem from a hand-built mix has lost the one thing provenance
is for. `StemProvenance` accepts both prefixes and reports which it read.

Adoption **refuses a file that already carries provenance**, in either dialect. Overwriting
StemForge's would lose which separation a stem came from, and re-adopting an adopted file is a
relabel, not a second write.

This makes [ADR 0003](0003-crateside-may-write-abletons-tag-store.md)'s "crateside writes exactly
one thing" false as written. It is now two, and the second is narrower than the first: one tag, on
one file, at the moment the user asks for it, and never again.

## Consequences

The three red tests go green the moment the file is adopted, which is the right shape of proof:
the file was invisible, and now it is not.

A track can now hold a sample that no plan can reconstruct. If the user deletes an adopted sample,
it is gone — crateside will not notice and could not help if it did. That is a real sharp edge and
it is accepted: the alternative is crateside keeping copies of files it did not make, which is a
backup system and not this.

## Deliberately not done

**Building the merged file.** The user described the procedure well enough to automate — find the
stretches where the vocal stem is silent, measure the average level difference between the source
and the instrumental over those stretches, and emit the source there and the gain-matched stem
elsewhere. That is a real feature and it is not this one. This ADR only lets such a file exist
once it has been made by hand; making it is [#39](https://github.com/Bamo16/crateside/issues/39).

**A UI to adopt.** The domain and the writer land first so the model can be argued with before a
form is built on top of it.

## A re-cut recycles rather than deletes — 2026-09-19

Hours after this was written, a re-cut deleted the first adopted sample there had ever been. The
cause was a missed construction site and is fixed. What the incident exposed is separate: the
removal used `File.Delete`, which does not use the recycle bin, so the file was only recovered
because the user happened to have exported a second copy elsewhere.

This ADR already said an adopted sample is the one thing in a track nothing can make again, and
then left its deletion permanent. That was an oversight rather than a decision.

Everything a re-cut, a source swap or a handoff removes now goes to the recycle bin
(`RecycleBin.Remove`). The *Consequences* above still hold for a file the user deletes themselves
— crateside does not keep copies of files it did not make — but crateside's own removals are now
undoable.

It is deliberately not narrowed to adopted samples. A preset stem can be cut again, so recycling
it is only a convenience, but two rules for "what happens when crateside deletes your audio" is a
worse thing to hold in your head than one. The shell call is silent by construction: a dialog in a
drain loop would block the thread rather than reach anyone, which is why
`Microsoft.VisualBasic`'s helper is not used.

## The form — 2026-09-24

Built for [#44](https://github.com/Bamo16/crateside/issues/44), while the user was away. The
calls below were taken as defaults and are for their review:

- **Adopted where it is.** The file is written in whichever root it is in. A finished file in
  `Mashup Samples` is not routed back through staging, which would move a warped file for nothing.
- **The user names the track** by opening the form on its row. Nothing is inferred from the
  filename.
- **The file is picked from those carrying no provenance at all**, in either root. There were 3 on
  2026-09-24.
- **The label is the bracketed suffix of the filename** and cannot be edited on the form. A file
  with no suffix is refused until it is renamed.
- **The type is chosen as for an added source** ([ADR 0028](0028-a-file-that-was-never-cut-carries-its-type-in-its-comment.md)),
  pre-selected from the label.
- **Backing is not triggered.** Once the file carries provenance and its track has a verdict,
  the next backing pass keys it.

Fixing a misspelt name at the same time, which #44 suggested for the High Contrast file, is not
done. Renaming a track's files is already its own gesture.
