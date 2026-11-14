# 0023 — a key with no store entry is not durable

- Status: Accepted
- Date: 2026-09-22
- Amends [ADR 0003](0003-crateside-may-write-abletons-tag-store.md): crateside writes
  `Mashup Samples`' tag store as well as `Sample Staging`'s.
- Settles the question [ADR 0013](0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)'s
  2026-09-12 amendment opened, and the cause [#24](https://github.com/Bamo16/crateside/issues/24)
  could not name.

## Context

ADR 0013's amendment established that writing a root's tag store makes Ableton reconcile that
root's index keywords down to match it — so a keyword in the index with no entry in the store is
destroyed by the next write. It called the gap a vulnerability and left two things unknown: what
opens it, and what closes it.

Both are now measured, on 2026-09-22, against a library that had lost the keys of 191 files.

**What opens it: every import.** A file is tagged in `Sample Staging`, where crateside writes the
store, so its key is backed. Moving it to `Mashup Samples` carries the keyword across *in the
index* and writes no entry into the destination store. Measured live, on a file moved from
Ableton's own browser while the scan was running:

```
Sample Staging   on disk: no    store entry: yes   index: no
Mashup Samples   on disk: yes   store entry: NO    index: YES   Key|F  Key|Minor  Type|Vocal
```

Nothing looks wrong at this point. The browser shows the key, because the browser shows the
index. The file is simply no longer backed by anything.

**What closes it: an ordinary hand-applied tag.** `Mashup Samples`' store held 606 entries; in
document order the last four were the only ones added since the 09-12 repair wrote 602, and one
of them carried `Type|Vocal` with no key at all — which crateside cannot emit. They were applied
by hand in the browser. Each such tag makes Ableton rewrite the store and reconcile the root, and
the write of 2026-09-20 19:07 destroyed every unbacked key in the library at once.

The correlation over all 796 files is exact: an entry in the store and a key in the index are the
same fact. There were no index-only files left and no store-only files at all.

So the loss is not intermittent and not Ableton misbehaving. **Tagging a file and keeping a file
tagged are two different acts, and crateside was only doing the first.**

## Decision

**A store entry is what makes a key durable. Writing the key is not enough; the key has to be
backed.**

Backing is a separate act from tagging, and the two differ in every dimension that matters:

- **Tagging asks what a file's key should be. Backing asks whether that key will survive.** A
  file whose key is in the index and not the store is fully tagged and entirely at risk.
- **Tagging is per track. Backing is per root, all at once.** This is forced, not chosen: each
  write reconciles the whole root, so writing one unbacked file at a time would have the first
  write destroy the keys of every file not yet reached. **A backing pass writes every unbacked
  file in a single `Apply`, or it is worse than doing nothing.**
- **Tagging happens in `Sample Staging`. Backing happens wherever files come to rest.** Today
  that is `Mashup Samples`, which is the only root a file arrives in by being moved.

### crateside writes `Mashup Samples`' store

ADR 0003 permitted `Sample Staging` and nothing else, on the grounds that writing a root should
be measured before it is routine. It has now been measured three times — the isolated test folder
ADR 0003 itself used, and two hand repairs of this root, on 09-12 and today — and the mechanism is
understood rather than observed. The restriction has done its job and is lifted for this root.

It is lifted for **this root**, not for roots in general. `Mashup Samples` earns it by being where
the gap opens; a root crateside has no files in has nothing to back.

### What a backing pass may touch

**Only files with no entry at all.** It adds; it never modifies or removes an entry that exists.

This is narrower than it could be — an entry whose key disagrees with the ledger stays wrong, and
a partial entry stays partial — and the narrowness is the point. The store is where the user's own
hand tags land. A pass that runs unattended and can overrule them is a different and much larger
proposition than one whose worst case is a file it skipped.

The known cost of the rule, today, is one file: `Lil Wayne - A Milli (A Cappella)`, whose
hand-applied entry carries a `Type` and no key. Backing will not touch it and the audit will go on
reporting it, correctly.

### It does not read the index

A backing pass needs the files on disk, the store's entries, and the ledger. It never asks what
the index holds, because the answer does not change what it does: an unbacked key is perishable
and an unbacked file that has already lost its key is untagged, and one write fixes both.

That is also what keeps it terminating, and it is the same reasoning
[ADR 0017](0017-each-root-is-audited-from-the-store-that-holds-its-tags.md) applied to the tagging
stage. **ADR 0017 is unchanged**: the audit still reads `Mashup Samples` from the index, because
the audit asks what Ableton shows, and that is still the index. The two questions have different
right answers and now have different readers.

## Consequences

The library stops losing keys, rather than being repaired after it has. The 09-12 repair was a
reset that bought ten days; this is the thing that makes a reset unnecessary.

**A hand-applied tag in `Mashup Samples` stops being destructive** — not because anything about
Ableton changed, but because there will be nothing unbacked left for it to collect.

crateside now writes two stores and one audio tag. ADR 0003's "crateside writes exactly one
thing" is now false in both of its clauses; ADR 0022 falsified the second and this falsifies the
first. What survives is the reasoning: each is narrow, each was measured before it was permitted,
and neither invents a keyword Ableton does not already know.

A backing write is still subject to the same staleness check as any other — the store's timestamp
is re-read immediately before the swap — so a pass that races Ableton loses and retries, which is
the existing behavior and needs no change.

## Deliberately not done

**Finding out why Ableton reconciles downward at all.** It is Ableton's design, it is consistent,
and knowing the reason would not change a line of this.

**Backing `Sample Staging`.** It has no gap: every key there is one crateside wrote into the store
itself, and a hand tag applied there lands in the store the same way. The pass is written against
a root rather than hardcoded, so pointing it at another one later is configuration, not a rewrite.

**Repairing disagreements.** The audit reports them and a human decides, which is ADR 0011's
instinct and still right. Nothing here automates a judgment call.

## Amendment, 2026-09-24 — a backing pass may add one genre to an entry that exists

*What a backing pass may touch* said: only files with no entry at all. That still holds for
**keys**. It no longer holds for **genre** ([ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md)).

The user decided on 2026-09-24 that genre is library-wide, not staging-only (#2). That means a
file already in `Mashup Samples`, with a hand-tagged or backed entry, has to gain a `Genre|`
keyword. The rule now reads:

- **An entry with no key** is still given one, exactly as before.
- **An entry that carries a key** may gain the song's **settled** genre. Everything else in its bag
  is carried across, since the writer replaces the whole bag. If the entry already holds a
  different `Genre|` keyword, that one is replaced, because the *Genre* group is crateside's own.
- **A genre the user took off** (a hidden keyword in the store) is never put back, like a key.
- **A keyless entry gets no genre.** In staging, the tagging stage's next write would replace
  its bag and drop the genre again.
- The pass now also runs over **`Sample Staging`**, for the genre only. Staging's keys stay the
  tagging stage's to write, so the *Deliberately not done* entry above still stands for keys.

It is still one `Apply` per root, for the same reason as before.
