# The non-Claude judge with web search

*Note, 2026-10-15, frozen. For [#108](https://github.com/Bamo16/crateside/issues/108); decides
nothing itself. A repeat of
[the run without search](2026-10-15-a-non-claude-judge-on-ten-judged-tracks.md) on the same ten
tracks, same runner, prompt and flags, after the endpoint gained an implementation of Anthropic's
server-side `web_search` tool backed by DeepSeek's own search. One run per track. Agreement with
Claude, not accuracy.*

## Result

| track | Claude | non-Claude | sources | s |
|---|---|---|---|---|
| Sammy Virji, Chris Lake, RoRo - 925 | G minor, MEDIUM | G minor, MEDIUM | 2 | 94 |
| Telefon Tel Aviv - Fahrenheit Fair Enough | D minor, MEDIUM | D minor, MEDIUM | 2 | 121 |
| Johnny Pearson - Action in Memphis | C minor, MEDIUM | C minor, MEDIUM | 0 | 126 |
| Whitney Houston - How Will I Know | F# major, HIGH | F# major, HIGH | 4 | 201 |
| Monday Night Football Theme | C major, HIGH | C major, HIGH | 1 | 92 |
| Nelly - Hot In Herre | E minor dorian, HIGH | E minor, HIGH, **not recorded** | | 46 |
| GYARI - アカリがやってきたぞっ | G minor dorian, HIGH | G minor, MEDIUM, **not recorded** | | 70 |
| Rob Araujo - Nineteen | F minor, MEDIUM | refused: three detectors, three tonics | | 12 |
| Kanye West, Pusha T - Runaway | E major, HIGH | stopped after one turn | | 5 |
| Three 6 Mafia, Unk - I'd Rather | B minor, MEDIUM | stopped after two turns | | 16 |

- **Search works** (*measured*): `WebSearch` returns cited pages (Beatport, Sheet Music Direct,
  Musicnotes, Sheet Music Plus, getsongkey, AcousticBrainz), and HIGH verdicts came back where a
  source agreed. How Will I Know, a three-way detector split that was refused twice without
  search, was settled from two published transcriptions.
- **Five recorded, all five agree with Claude; seven stated, all seven agree** (*measured*). No
  key disagreement in any run of either note.
- **The failures are now about finishing, not evidence.** Two sessions stated a verdict and ended
  without running the recorder. Two ended mid-thought ("let me read the reference material…")
  with no tool call. One refused a genuine three-way split, which Claude called MEDIUM.
  Under the runner, all five are "finished without recording a verdict", a strike each.
- **The provider session held** for the whole batch (about 13 minutes). An earlier session had
  expired between runs, and the browser it came from was signed out too, so the provider ended
  it; why is not known (*measured once each*).
