# Partners and the modal verdict

*Note, 2026-10-17, frozen. Asks how [ADR 0055](../adr/0055-crateside-owns-the-key-judge-and-its-ledger.md)
(a verdict is a tonal center; the tag is its collapse) and keydetect-harness's
[ADR 0001](https://github.com/Bamo16/keydetect-harness/blob/main/docs/adr/0001-ground-truth-is-a-tonal-center-scored-by-its-collapse.md)
and [glossary](https://github.com/Bamo16/keydetect-harness/blob/main/GLOSSARY.md) (harness `7357c59`)
bear on the Partners tool. Decides nothing. Each claim is tagged **MEASURED** (run against
crateside's code at `ea4a049`), **READ** (traced in the code or docs, not run), **INFERRED** or
**GUESSED**. Written in a cloud session with no ledger, no Ableton roots and no AbleKit checkout,
so nothing here was run against the real library; what that leaves open is listed at the end.*

## How it was measured

The six files in `src/Crateside.Core/Pairing/` were compiled unchanged into a throwaway console
project and called directly: `Partners.For`, `Partners.Between`, `MusicKey.FromString`. Scales
were built as the seven rotations of the major scale, and the same-notes key found by set
equality of pitch classes, which reproduces `ModeText.SameNotes`'s table (brief 17) for all seven
modes on A.

"Notes shared" below counts how many of a tonal center's seven pitch classes a partner's scale
holds **after** the partner is moved by its `Shift`. It is a proxy for consonance, not a
measurement of whether two tracks pair. Nobody has measured that.

## 1. What Partners takes as its source

**The most common Key tag on the song's samples, never the verdict.** READ.

- `Tools.razor` `Select(track)` sets `_keyName = track.Key?.LedgerName`; `Own` is
  `MusicKey.FromString(_keyName)`; `Working` is `Own` transposed by the Re-Pitch shift, or its
  `Relative` when that pill is on; `Recalculate` calls `Partners.For(Working, _partnersMax)`.
- `LibrarySong.Key` is set in `LibraryKeyReader.Song` from `MostCommon(samples, s => s.Tags?.Key)`:
  the tags Ableton holds on the files. `LibrarySong.Run` carries the ledger run, with its `Center`,
  `Sections` and `Mixed`, and Tools never reads it.
- A typed key goes through the same `MusicKey.FromString`, which has no modes:
  `FromString("A Dorian")`, `"A dorian"` and `"A mixolydian"` all return null, so the tool shows
  *Pick a track, or a key*. MEASURED.
- `MusicKey` and `Mode` know only the 24 keys. Nothing in `Pairing/` references `TonalCenter`. READ.

So for a modal verdict the source is the collapse, at one remove: it is the tag written to the
samples, which equals the collapse only while the samples were tagged from the current verdict.

## 2. Shared tonic or shared notes

### What the shift rule actually is

`MashupPartner.Create` moves the partner so **its tonic lands on the source's tonic, or a fourth
or fifth from it**, whatever either key's mode. Over all 24 sources × 24 candidates, every pairing
lands the partner's tonic at +0, +5 or +7 from the source's, except the relative, which is
special-cased to shift 0 on the comment *same notes, other tonic* (and lands at +3 or +9).
MEASURED.

So Partners is a tonic-alignment model with one same-notes exception, and that exception is
defined on the 24-key form: the tag's relative. One consequence for plain keys too: the parallel
key (A major for A minor) is offered at shift 0 for **24 of 24** sources, labeled *Modulation*,
and holds 4 of the source's 7 notes. MEASURED.

### Where the same-notes key lands for a modal source

From `Partners.Between(collapse, same-notes key)`, the same for all 12 tonics of each mode. MEASURED.

| center (on A) | tag | same-notes key (harness: major, minor) | major one from the tag | minor one from the tag |
|---|---|---|---|---|
| Ionian | 11B | — (the relative) | — | — |
| Lydian | 11B | E major 12B, C♯ minor 12A | 0, Energy Rise | **+1**, Mood Shift |
| Mixolydian | 11B | D major 10B, B minor 10A | 0, Relaxed | **−2**, Mood Shift |
| Aeolian | 8A | — (the relative) | — | — |
| Dorian | 8A | G major 9B, E minor 9A | **+2**, Mood Shift | 0, Energy Rise |
| Phrygian | 8A | F major 7B, D minor 7A | **−1**, Mood Shift | 0, Relaxed |
| Locrian | 8A | B♭ major 6B, G minor 6A | **−1**, Modulation | **+2**, Modulation |

In every mode but Ionian and Aeolian, at least one same-notes key is told to shift, and the shift
moves it **off** the center's notes. For A Dorian, Partners places G major 18th of 24, says pitch
it +2 (landing in A major, 5 of 7 notes), and at the default `±1` does not list it at all; played
unshifted it holds all 7. For A Phrygian it says pitch F major −1 (E major, 2 of 7), against 7 of 7
unshifted. MEASURED.

The same-notes keys Partners does keep at shift 0 are kept only because their tonic happens to be
a fourth or fifth from the source's (Dorian's E minor, Mixolydian's D major), not because their
notes match. MEASURED (the tonic intervals), INFERRED (that this is the reason; it follows from the
rule above).

### Rebasing on the same-notes key is not a plain win

Running Partners from the same-notes key instead of the tag (brief 17's handoff, *1d*) changes
what is offered rather than improving it. For A Dorian at shift 0:

| basis | offered at shift 0 |
|---|---|
| A minor (tag) | A minor, C major, E minor, D minor, D major, A major, E major |
| G major (same notes) | G major, E minor, C major, D major, D minor, G minor, C minor |

Four of seven are common. The tag basis keeps the keys tonic-aligned to A; the same-notes basis
keeps the two keys with A Dorian's notes and loses A minor and A major. Averaged over the 12 tonics,
notes shared with the center by the partners offered at shift 0 (mean, of 7). MEASURED:

| mode | from the tag | from the same-notes key |
|---|---|---|
| Ionian, Aeolian | 5.43 | 5.43 |
| Dorian | 5.57 | 5.43 |
| Mixolydian | 5.57 | 5.43 |
| Lydian | 4.71 | 5.43 |
| Phrygian | 4.71 | 5.43 |
| Locrian | 3.86 | 5.43 |

By the notes-shared proxy, rebasing helps Lydian, Phrygian and Locrian and costs Dorian and
Mixolydian a little. Whether the proxy predicts what pairs is GUESSED: Partners itself does not
optimize it (it offers the parallel, at 4 of 7, for every key).

### Which pairs?

**Not measured, and not measurable from this session.** No dataset records which pairings worked;
the ledger holds keys, not mashups. The code's own reasoning points to notes: the only shift-0
exception it makes is justified as *same notes, other tonic*, and for a modal source the key that
satisfies that sentence is the same-notes key, not the tag's relative. INFERRED.

### Is *Relative* misleading for a modal source?

Yes, in two places. READ, with the counts MEASURED:

- The *relative* pill's row meaning, `PartnersTool.razor` `Meaning`: *the same notes, the other
  mode*. For a modal source the tag's relative holds 6 of the center's 7 notes (Dorian, Phrygian,
  Lydian, Mixolydian) or 5 (Locrian). The same sentence is `MusicKey.Relative`'s summary, which is
  true of the 24 keys it is written for.
- The Tools page never shows the mode, so the user sees *A minor · relative C major* with no way
  to know the song plays G major's notes.

## 3. Sections

- **Nothing to measure yet.** Brief 17 records no section history in the ledger as of 2026-10-16;
  sections arrive with the rewritten judge and the windows form. Not re-counted here (no ledger).
  READ.
- `LedgerRun.Sections` already holds each section's center, so a section's collapse and
  same-notes key are computable with what exists. READ.
- The partner side cannot see sections: the browser filters on each file's single Key tag, which
  follows the partner's primary center. Offering partners per section of the source finds files
  whose *primary* is compatible with that section, which is what a mashup built on the source's
  chorus needs. INFERRED.
- A section's partners are a different basis, as the relative and Re-Pitch already are, so they
  fit the existing basis pills rather than a second table. INFERRED.

## 4. Mixed

- The collapse follows the harmony, and Partners works from the tag, so it already pairs the
  harmony. READ.
- The melody's quality is the parallel of the tag, which Partners offers at shift 0 for every
  source (24 of 24), under the label *Modulation*. MEASURED. So a mixed song's melody is already
  served, but the row does not say it is the parallel.
- "Same notes" has no single answer for a mixed center (two scales on one tonic), and
  `ModeText.Reason` already gives mixed its own sentence with no same-notes key. A same-notes basis
  should not be offered for a mixed verdict. INFERRED.
- Which stem is being paired (the vocal carries the melody, the instrumental the harmony) would
  decide which quality matters, and Partners does not know the stem. GUESSED that this matters
  in practice; not measured.

## 5. Vocabulary

Where crateside's `CONTEXT.md` and code disagree with each other or with the harness's glossary.
READ throughout.

| term | harness | crateside | drift |
|---|---|---|---|
| **Same-notes key** | a defined term: the major *or minor* key sharing the center's notes, other than the collapse (G major and E minor for A Dorian); for Ionian and Aeolian, the relative | no `CONTEXT.md` entry; *Collapse* says "the key with the same notes"; `ModeText.SameNotes` and ADR 0055's 2026-10-16 addendum name only the major | crateside uses the harness's idea without the term, and drops the minor one |
| **Relative** | the collapse's relative, for every mode (an outcome class) | `MusicKey.Relative`: "the same notes in the other mode"; Partners' *Relative (Epic)*, meaning *the same notes, the other mode*; no `CONTEXT.md` entry | agrees on *which* key; crateside's gloss is false for a modal source |
| **Parallel** | the collapse's tonic, the other quality: *G minor* for *G major* | no term; Partners files it under *Modulation*. `LedgerRun.IsCollapsed`'s summary calls the collapse "its parallel major or minor" (the textbook sense: A minor is A Dorian's parallel minor) | the same word for two different keys: for A Dorian, crateside's "parallel" is A minor, the harness's is A major |
| **Modulation** | *avoid* for a part of a song; the corpus field `modulation` became `sections` | `Relationship.Modulation`, the catch-all for "further round the wheel", parallel included; `CONTEXT.md` already flags it under *Vibe* | collides with *Section* now that crateside has sections: a partner row reading *Modulation* suggests a key change |
| **Subdominant/dominant** | a fourth or fifth off, mode unstated; for Mixolydian the IV is also the same-notes key | *Relaxed* / *Energy Rise*, same mode only | crateside's names are moods for the same idea; a fourth or fifth in the other mode (D major for A minor) is *Modulation* |
| **Relationship** (crateside) | — | `CONTEXT.md`: "the same key, its relative, a step around the circle in either direction, or a **borrowing**" | the code has no borrowing; it has *Mood Shift* and *Modulation* |
| **Adjacent on the wheel** | — | ADR 0055's addendum: Dorian's 9B and Phrygian's 7B are not adjacent to 8A; `CONTEXT.md` *Collapse*: "the relative major's neighbor"; Partners: circle distance 1, *Mood Shift* | "adjacent" is undefined; the code says these are one step away |
| **One note away** | — | `LedgerRun.IsCollapsed`: the tag is "one note away from what it plays" | Locrian's tag differs by two notes (5 of 7 shared). MEASURED |

## Options for the maintainer

None is decided. Every option except the last needs Tools to read `LibrarySong.Run.Center`
beside the sample tag, since Tools has no mode today.

1. **Generalize the shift-0 exception from "the tag's relative" to "a key holding the center's
   notes".** Partners stays tonic-aligned and keeps the tag as its basis, but G major and E minor
   both come back at shift 0 for A Dorian, labeled as same-notes. For a plain source the only
   other key holding its notes is its relative, so its output is unchanged and the cmdlet parity
   fixture still describes it (by construction; not run). A change to `MashupPartner.Create`'s
   inputs.
2. **Handoff 1d's third basis pill**, *G maj same notes*, re-running Partners from that key. No
   change to `Pairing/`. It trades tonic-aligned partners for notes-aligned ones (the table under
   §2) and leaves the tag basis's +2 advice for G major in place.
3. **Wording only**: for a modal source the relative pill's meaning stops saying *the same notes*,
   and the basis line names the mode (handoff 1d: *A min (8A), the tag of A Dorian.*).
4. **Sections as further basis pills**, once the ledger holds sections; **mixed** gets no
   same-notes basis, and the parallel row says it is the parallel.
5. **Vocabulary**, independent of the rest: `CONTEXT.md` entries for *same-notes key*, *relative*
   and *parallel* matching the harness, a fix to *Relationship*'s "borrowing", and the
   *Modulation* rename the *Vibe* entry already calls a deliberate behavior change.

## What this leaves untested

All of it needs the maintainer's machine:

- Whether any song's sample tag differs from its current verdict's collapse (Tools reads the tag).
- How many songs Partners would treat differently: brief 17 counted 12 Dorian, 5 Mixolydian and
  3 Phrygian among 519 songs on 2026-10-16. Dorian and Phrygian (15) are the ones whose major
  same-notes key Partners moves; Mixolydian's move only the minor one. Not re-counted here.
- Section and mixed counts in the ledger.
- `PartnersTests` and `PsMashupParityTests` were not run; the test project needs AbleKit. The
  pairing code was compiled unchanged, so the figures above are the code's, but the parity
  fixture was not checked against it in this session.
- Whether any of this changes what sounds right. Nothing measured here is a listening result.

## Reproducing

A console project compiling `src/Crateside.Core/Pairing/*.cs` with this `Program.cs` excerpt
(the rest only printed tables):

```csharp
int[] Major = [0, 2, 4, 5, 7, 9, 11];
HashSet<int> ScaleOf(int tonic, int rot) // rot: Ionian 0 … Locrian 6
{
    var parent = (tonic - Major[rot] + 12) % 12;
    return [.. Major.Select(i => (parent + i) % 12)];
}
HashSet<int> KeyScale(MusicKey k) => ScaleOf(k.Chromatic, k.Mode == Mode.Major ? 0 : 5);
MusicKey Collapse(int tonic, int rot) =>
    MusicKey.AllKeys.Single(k => k.Chromatic == tonic
        && k.Mode == (rot is 0 or 3 or 4 ? Mode.Major : Mode.Minor));
MusicKey SameNotes(int tonic, int rot) =>
    MusicKey.AllKeys.First(k => k.Mode == Mode.Major && KeyScale(k).SetEquals(ScaleOf(tonic, rot)));
HashSet<int> Landed(MashupPartner p) => [.. KeyScale(p.Key).Select(n => (n + p.Shift + 12) % 12)];
// notes shared: ScaleOf(tonic, rot).Intersect(Landed(p)).Count()
```
