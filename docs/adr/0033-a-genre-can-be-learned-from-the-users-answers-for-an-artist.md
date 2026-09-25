# 0033 — a genre can be learned from the user's answers for an artist

- Status: Accepted
- Date: 2026-09-25
- Part of [#61](https://github.com/Bamo16/crateside/issues/61). Amends [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md).

## Context

ADR 0030 writes only a genre the user settled, and the user settles almost none: 1 of 412 on
2026-09-25. The user wants genre as automated as possible without crateside silently tagging
things wrong. They also described how they decide it themselves:

> I tend to generalize sometimes based on artist [...] Kanye West, Lil Wayne, Lil Dicky where I
> would feel pretty confident simply treating every track of theirs to be hip hop.

## Decision

**When at least 3 of the user's own answers for an artist name one genre, and none names another
or "none of these", the artist's unanswered songs are settled to that genre.** The number 3 is
guessed.

- **The artist is the primary one**, the first credited (`ArtistCredit.Primary`). This is the
  first use of the primary-artist idea left open by ADR 0032.
- **Only the user's answers teach.** A learned genre (`GenreRecord.LearnedFrom`) never counts
  towards learning another, so one answer cannot snowball.
- **The user always wins.** Answering a learned song makes it their own, and a learned genre never
  replaces an answer.
- **It is taken back** when the user's answers stop supporting it. For example, a fourth answer
  says "none of these".
- **It is visible.** The strip says *from your answers for {artist}*.

The source-agreement half of #61 is not built. See the issue.

## Consequences

- Nothing changes until the user has answered 3 songs by one artist.
- An artist who genuinely spans genres learns nothing once the user answers one differently. That
  is the safe failure.
