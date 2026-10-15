# A non-Claude judge on ten tracks Claude had judged

*Note, 2026-10-15, frozen. For [#108](https://github.com/Bamo16/crateside/issues/108); decides
nothing itself. Measured on this machine: the verdict runner's exact
prompt, flags and tool list, pointed by environment variables at an Anthropic-compatible endpoint
serving DeepSeek's chat model, run from a copy of the keytag checkout with no git and with each
track's existing ledger lines removed first. Which DeepSeek model answered is not reported by the
endpoint, so this is not a result for DeepSeek's official API.*

## What the endpoint could and could not do

- **Plain messages and client-side tool calls work** (*measured*): a tool call and its result
  round-trip, `/detect-key` resolves, and the recorder writes a valid ledger line.
- **`WebSearch` fails on every call** with `400 Invalid or unsupported text/tool request`
  (*measured*). It is a server-side tool: the API provider runs the search, so any endpoint that
  does not implement it refuses it. DeepSeek's API documentation says its OpenAI-format Responses
  API ignores built-in web search (*owned, from the docs*); its Anthropic-format endpoint was not
  tried.
- **`WebFetch` works** (*measured*, outside the runner, which does not grant it): the page is
  fetched on this machine and summarised by the configured model. It read Wikipedia and a
  Hooktheory TheoryTab page in full, so keytag's `SOURCES.md`, which says Hooktheory returns 403
  to `WebFetch`, is out of date. DuckDuckGo's HTML results returned a captcha.
- **Usage reads zero** (*measured*): no token counts, `total_cost_usd` 0. `--max-budget-usd`
  therefore never trips on this backend.
- **The provider's own search** (switched on for every request at the endpoint) returned real,
  cited URLs when asked directly, but **no judging run used it**: every recorded verdict below has
  zero sources, and each run's summary says no human source was reached (*measured*).

## Result

Ten tracks with a Claude verdict, one run each; the six without a verdict were run once more.
The comparison is **agreement with Claude, not accuracy**.

| track | Claude | non-Claude | run 1 (s) | run 2 (s) |
|---|---|---|---|---|
| Sammy Virji, Chris Lake, RoRo - 925 | G minor, MEDIUM | G minor, MEDIUM | 148 | |
| GYARI - アカリがやってきたぞっ | G minor dorian, HIGH | G minor, MEDIUM | 128 | |
| Rob Araujo - Nineteen | F minor, MEDIUM | F minor, MEDIUM | 117 | |
| Monday Night Football Theme | C major, HIGH | C major, MEDIUM | 170 | |
| Telefon Tel Aviv - Fahrenheit Fair Enough | D minor, MEDIUM | D minor, MEDIUM | refused, 32 | 108 |
| Johnny Pearson - Action in Memphis | C minor, MEDIUM | C minor, MEDIUM | refused, 38 | 26 |
| Kanye West, Pusha T - Runaway | E major, HIGH | E major, MEDIUM | refused, 40 | 38 |
| Nelly - Hot In Herre | E minor dorian, HIGH | none | refused, 56 | refused, 44 |
| Whitney Houston - How Will I Know | F# major, HIGH | none | stopped after one turn, 5 | refused, 73 |
| Three 6 Mafia, Unk - I'd Rather | B minor, MEDIUM | none | endpoint 502, 482 | endpoint 502, 271 |

- **7 agree on the key, 0 disagree, 3 without a verdict** (*measured*).
- **Every verdict is MEDIUM**, because the skill reserves HIGH for a run with a human source.
  No dorian call was made where Claude made two.
- **The refusals are the skill working as written.** On a relative or parallel tie it requires
  corroboration, and with search failing the session said so and recorded nothing. Two sessions
  also tried the skill's `curl` recipe for Hooktheory and hit the runner's `Bash(uv run:*)`
  allowance. Three refusals became verdicts on a second run, with no new information: the
  difference is run-to-run variance, not search.
- **Wall time:** 26 to 170 s for a recorded verdict (*measured*). Claude's was not timed here.

## What it suggests

Search is a real input on the hard tracks, not decoration: without it about a third of these
tracks get no verdict. A judge that does not depend on a provider's server-side tools needs the
corroborating pages put in front of it, which crateside can fetch itself from known sites by
artist and title.
