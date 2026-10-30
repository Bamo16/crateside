# 0032 — familiarity is read off reach: the artist first, then the song

- Status: Accepted
- Date: 2026-09-25
- Settles [#52](https://github.com/Bamo16/crateside/issues/52). Follows [ADR 0030](0030-genre-is-a-tag-group-of-our-own-settled-by-the-user.md).

## Context

The user builds a mashup from one half the listener almost certainly knows and one half they
probably do not, usually something the user likes: *"I like using the well-known sample as a
vehicle to share something new with the listener."* Having picked one half, they want to filter
for the other.

Measured on #52 (Deezer's public API, no key):

- **Artist fans separate the user's examples** by about an order of magnitude. Obscure ones sit at
  ≤18k (Moe Shop, Snail's House) and mainstream ones at ≥137k (Village People and up). 42 ledger
  artists (2026-09-24) fall between.
- **The song's own rank catches what the artist misses.** A one-hit wonder by a small artist,
  such as Cali Swag District – *Teach Me How to Dougie* (33k fans), has a song rank of 625k. The
  user's obscure examples all rank under 350k.

The user, 2026-09-25: judge by the artist, *"but if the artist is not mainstream, we also check the
specific song too."* They also said the rule will need tuning, and that nobody will know how until
it is in use.

## Decision

**A tag group of crateside's own, `Familiarity`, with two values, `Mainstream` and `Obscure`.
They are read off Deezer's numbers where those are clear, and the user answers the rest.**

- **The rule** (`FamiliarityVocabulary.FromReach`): artist ≥150k fans → Mainstream; else song rank
  ≥600k → Mainstream; else artist <20k fans and song rank <400k → Obscure; else ask the user. On
  the 2026-09-25 ledger this settled 269 of 337 songs and asked about 68. That was measured with the
  first credited artist, not the most-followed one, so the built rule may ask about slightly fewer.
- **Clear values are written without asking.** Unlike genre, a value the numbers settle is
  written as if settled. The user's own answer always outranks it, and survives a new
  measurement.
- **Which artist:** the most-followed of every artist Deezer credits on the matched song, featured
  artists included. That catches a big-name feature: Estelle – *American Boy* credits Kanye West. The lookup
  searches by the song and reads its credits, so names that do not split cleanly ("Polo & Pan",
  "Jay-Z") are not guessed at.
- **Written by the backing pass** beside genre, under the same rules: only onto a keyed entry, it
  replaces only a keyword of its own group, and a value the user took off by hand is never put back.

## Consequences

- The cut points (150k, 20k, 600k, 400k) are **guessed**. They were placed only to bracket the
  user's examples, and they live in one method so tuning them touches nothing else.
- A deep cut by a big artist (Eminem – *Medicine Ball*, song rank 265k) reads as Mainstream, because the
  artist decides first. That is what the user described.
- **Not decided:** whether contribution matters, e.g. a big name who only produced the song. Deezer
  credits only main and featured artists, so today the question does not arise. Whether crateside
  should have a *primary artist* concept, shared with file naming (#53), is left open.
- **Unmeasured**, as for genre: that Ableton makes a group from the first `Familiarity|` keyword.
