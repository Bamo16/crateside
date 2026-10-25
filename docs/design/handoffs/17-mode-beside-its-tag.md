# Handoff 17: a verdict's mode, shown beside the tag

Brief 17, issue #114, ADR 0055. Canvas: `Crateside Mode Beside Its Tag.dc.html` (1a–1d).

**The rule:** the tag stays where it is, at its size and in its place. A modal verdict adds its
mode *before* it. Where there is room this is the arrow (`A Dorian → A min`); where there isn't,
an outlined abbreviation after the tag with the arrow on hover. Plain major and minor, Ionian and
Aeolian included, change nothing. No color anywhere.

## Words

One formatter (call it `ModeText`) writes all of these, and `KeyText.Spell` still spells the tag.

- **Arrow**: `{tonic} {Mode} → {tag}`, mono: `A Dorian → A min`. Mixed: `A mixed → A min`.
- **Reason**: `{Mode} is a {major|minor} mode, so it is tagged by its tonic. Same notes as {key} ({camelot}).`
  Mixed: `Major melody over minor harmony; tagged by the harmony.` (or the reverse). Say nothing
  about distance on the wheel.
- **Abbreviation** (Tracks): `Dor` `Phr` `Lyd` `Mixo` `Loc`; mixed `maj/min`.
- **A source's stated key**: the mode in full (`G Mixolydian`), not `G major (mixolydian)`.

| mode | tag | same notes as |
|---|---|---|
| Lydian | A maj | E major (12B) |
| Mixolydian | A maj | D major (10B) |
| Dorian | A min | G major (9B) |
| Phrygian | A min | F major (7B) |
| Locrian | A min | B♭ major (6B) |

Ionian and Aeolian are drawn as plain major and minor.

## 1a Tracks row

- The Key column grows to **112px**. The tag stays mono 13/500 at the cell's left edge.
- Modal: 5px after the tag, the abbreviation in mono 10.5/500, `padding 2px 4px 3px`, radius 3,
  1px `--text` at 45% border, ink `--text` 85%. Outlined like a corroborating badge.
- Hover on the cell (`title` is fine) gives the arrow and the reason. With sections, add
  `Also E minor (chorus), C Lydian (bridge).`
- Sections get no mark in the row: it shows the primary center, the one the tag follows.

## 1b Track details band, Key cell

Top to bottom:
1. `KEY` label.
2. **Lead line** (modal only): the arrow's left half, mono 18/500 `--text`, with `→` at `--text-60`.
3. The tag at 52px/600, unchanged.
4. **Reason**, 12.5px `--text-75`. Replaces `really A Dorian, tagged as A min`.
5. **Sections** (if any): a hairline, then one line per other center, label (52px, Sans 12.5
   `--text-60`) and key in mono 12.5/500 with its own arrow where it's modal (`C Lydian → C maj`).
6. The usual sub-line (`tagged A min in Ableton on 2 of 2 samples`). With sections it reads
   `the verse, and the tag follows it`.

The band's other cells justify to the bottom (`justify-content:flex-end`) so the 52px figures
stay level when the Key cell is taller. *Why …* in Where it is reads `Why A Dorian ↓`.

**Room for no tonal center:** the same three lines give `no tonal center →`, `—` and a reason.

## 1c Why this key, with sections

- **Centers** block before Sources. Where sections have times, a strip on the track's time axis
  (34px tall, radius 3): the primary filled at `--text` 16% with a 50% border, other centers
  outlined at 35%. Each block is labeled `verse · G Mixo` in mono 11, with times under the strip
  as in the warp view. A repeated section is drawn each time it occurs. Without times, no strip.
- Under it, one ruled line per center: label (90px) · key with arrow (mono 13/500) · note (`primary ·
  the tag follows it · 0:00, 1:10`, or the times).
- **Sources**: each row adds a **covers** chip under the site (neutral outline pill, 11px):
  `whole song`, `chorus only`. ✓/✗ is judged against what it covered, and the relation line
  says so: `agrees with the chorus`, `disagrees with the bridge (C Lydian), not the song`,
  `matches the chorus, not the song`.
- The heading is `Why {primary center}`, with the gist `three centers; the tag follows the verse`.

## 1d Partners (proposal, not this issue)

For a modal verdict, add a third basis pill after the relative: `G maj same notes`. Picking it
re-sorts from that basis, as the relative pill does now. The basis sentence names the mode:
`A min (8A), the tag of A Dorian.` Plain verdicts get no third pill. The wheel is unchanged.

## Not designed

The Dashboard's tallies (left room for). A fourth shape, *no tonal center*: not drawn, but nothing
above assumes a tonic.
