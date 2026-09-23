# 0026 — a write that cannot be attempted does not fail the read

- Status: Accepted
- Date: 2026-09-23
- Settles the first of the two questions [#34](https://github.com/Bamo16/crateside/issues/34) had
  left open, and fixes a stoppage found while measuring it.

## Context

`ConsumeAsync` marks entries as offered, then removes them:

```csharp
intake.MarkRemovalAttempted(entries.Select(item => item.VideoId), DateTimeOffset.UtcNow);

authorizer.WriteAccess.Record(await RemoveAsync(entries, cancellationToken));
```

Marking first is correct and is the fix for ADR 0021's quota burn. What nobody had traced is what
`RemoveAsync` does when consent has lapsed:

```csharp
if (await authorizer.GetCredentialAsync(cancellationToken) is not { } credential)
    throw new QueueUnavailableException(
        "Not authorized to modify the playlist. Grant access and try again."
    );
```

It **throws**. `ConsumeAsync` is awaited bare inside `ScanAsync`, so the exception leaves
`ScanAsync`, leaves `ReadAsync`, and every drainer catches it as `StageTick.Unavailable` — *"Queue
could not be read"*. Two consequences, neither intended:

- **Every stage stops**, on a write none of them needs. The playlist read itself had just
  succeeded; only the delete was refused.
- **The entries stay marked.** The mark is never cleared, so those entries are never offered again
  even though no delete was ever attempted for them.

The guard at the top of `ConsumeAsync` does not catch this. It tests `IsAuthorized`, which is
`IsConfigured && HasStoredToken` — and a *lapsed* token is still a stored one. The token lapses
roughly weekly while the OAuth client is unapproved, so this is the ordinary weekly state, not an
edge.

`GetCredentialAsync` already records the lapse through `WriteAccess.Lapsed(reason)`, and the toast
added in `4865099` already tells the user. Nothing was missing from the *reporting*; the throw was
being treated as a read failure.

## Decision

**`ConsumeAsync` catches `QueueUnavailableException`, clears the marks, and does not rethrow.**

Clearing is sound precisely because nothing reached the API: the credential was refused before the
first delete, so no quota was spent and the mark bought nothing. This is the case #34 identified as
free to retry, and it is the only failure that qualifies — a per-item `403` did reach the API, cost
its fifty units, and stays marked.

`IntakeStore.ClearRemovalAttempted` is the mirror of `MarkRemovalAttempted`; both now go through one
private setter so the two cannot drift.

Not rethrowing is the other half. A refused write says nothing about whether the playlist can be
read, and the stages that were stopped by it neither write nor care. The lapse is still reported,
through `WriteAccess`, which is where it belongs.

## Consequences

- With a lapsed token and `consumeOnArrival` on, draining continues. Entries accumulate in the
  playlist, unremoved and still offerable, and are consumed on the first scan after re-consent.
- The two entries #34 measured — `Light Years Away` and `Look On Down From The Bridge` — are **not**
  freed by this. They were marked on 2026-09-17 and this only clears marks at the moment a lapse is
  caught. Freeing them is a one-off, and it is worth knowing which of the 60 marked entries are
  genuinely stranded before touching any of them.
- **Not covered by a test end to end.** The path needs a playlist that reads successfully *and* a
  credential that cannot be obtained, and `QueueRemover` is sealed with no seam to make it throw.
  `ClearRemovalAttempted` is tested directly; the catch is not. Said plainly rather than left to be
  assumed — closing it means giving the remover an interface, which is a change for its own commit.
- Still open in #34: whether a `403` from the wrong account should be recorded distinctly from an
  expired token. `QueueRemover.Describe` already tells them apart and nothing yet acts on it.
