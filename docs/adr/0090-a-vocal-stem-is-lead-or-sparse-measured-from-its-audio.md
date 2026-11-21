# 0090 - a vocal stem is lead or sparse, measured from its audio

- Status: Accepted
- Date: 2026-11-09
- Built by [#194](https://github.com/Bamo16/crateside/issues/194) (the measurement) and [#195](https://github.com/Bamo16/crateside/issues/195) (the tagging).
- Follows [ADR 0087](0087-crateside-tags-every-song-and-the-person-corrects-in-crateside-or-in-live.md).

## Context

Some vocal stems cannot carry a mashup as its vocal: long gaps, little or no vocal in the chorus,
minimal vocals, mostly in EDM. They are still kept, for chopping into a mashup over the same song's
instrumental, and nothing in the browser tells the two kinds apart.

Live lets a built-in tag take a subtag: `Type|Vocal|Lead` and `Type|Vocal|Sparse` were created in
Live and set on two stems on 2026-11-09.

## Decision

**Every vocal stem is tagged `Type|Vocal|Lead` or `Type|Vocal|Sparse`, from a measurement of the
stem's audio. Nobody is asked.**

- **Lead**: usable as the vocal of a mashup. **Sparse**: kept for chops.
- **A property of the stem file**, not the song, written only on files whose Type is Vocal.
- **Measured**: the share of bars with vocal energy, and whether the loudest stretches of the song
  carry vocal. The cut points are not set here. They come from a measurement against the stems the
  person has tagged in Live, written up as a note, and nothing is tagged until the maintainer
  accepts them.
- **Corrected** in crateside or in Live, as ADR 0087 says.
- **Lyrics are not used.** No free source gives section labels, and LRCLIB's line timings say
  mostly what the stem already does. If the measurement misses choruses, lyrics get an issue of
  their own then.

## Consequences

- Filtering `Type|Vocal` still shows both kinds; `Lead` narrows to the usable ones.
- Nothing is written until around 30 stems have been tagged by hand and the cut points accepted.
