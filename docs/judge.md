# The judge

The verdict run hands one track's evidence to a headless Claude Code session (`claude -p`),
which weighs it, looks for human corroboration and writes a **verdict file**. crateside checks
the file and records it in the [ledger](ledger.md). Decided in
[ADR 0055](adr/0055-crateside-owns-the-key-judge-and-its-ledger.md).

## What the session gets

- **Instructions**: `src/Crateside.Core/Orchestration/Verdict/judge-prompt.md`, passed with
  `--append-system-prompt`. The backed-by figures are in its table, in one place.
- **A work order** on standard input: the track's title and upload, the evidence (carried
  inline when small enough, its path otherwise), where to write the verdict file, the artist
  and title to keep when the song was judged before, and how much the user's ear counts.
- **Tools**: `Read`, `Write`, `WebSearch` and `WebFetch`, nothing else. Its working directory
  is the track's working set, where the verdict file goes.

## The verdict file

`verdict.json` in the working set, UTF-8 JSON in the shape the instructions give. crateside
fills in what the session cannot know: the time, the upload, the input file, the evidence, and
the song identity (kept from an earlier run of the same upload). It is checked against the
[ledger's rules](ledger.md#what-is-refused) before anything is recorded.

A session that ends without a usable file is **resumed once** (`--resume` on the same session)
with "write the verdict file now" and what was wrong. A second failure is the track's: the
verdict stage counts it as a strike.

**Windows.** A WITHHELD verdict may ask for up to four windows, in seconds. crateside detects
over each, resumes the same session once with that evidence, and records whatever it writes
then. If nothing usable follows, the WITHHELD verdict is recorded as it was. The windows
detected over are kept with the run, and their evidence inside the run's evidence under
`windows`.

## Settings

Under `Judge` in `appsettings.json`, user-secrets or the environment (`Judge__Model`):

| setting | default | what it does |
|---|---|---|
| `Model` | `sonnet` | passed to `claude --model` |
| `Ear` | `Hint` | how much the user's own reading of a key counts: `Hint` (weighed, never raising confidence by itself) or `HumanSource` (counted like an authoritative source) |
| `Environment` | empty | variables set for the judge process only |

### The endpoint

With `Environment` empty, the judge runs on whatever Claude Code is logged in to. To run it
against another Anthropic-compatible API, set the variables Claude Code reads. For example, the
Anthropic API with its own key, kept in user-secrets rather than in a file in the repo:

```
dotnet user-secrets set "Judge:Environment:ANTHROPIC_API_KEY" "<your key>" --project src\Crateside\Crateside.csproj
```

and, for an Anthropic-compatible gateway rather than Anthropic's own API, its address in
`appsettings.json`:

```json
"Judge": {
  "Environment": {
    "ANTHROPIC_BASE_URL": "https://gateway.example.com"
  }
}
```

A backend is trusted only after its own agreement check against verdicts already recorded
(ADR 0055).

## What it costs

Each session and each resume spends Claude quota, about 37k tokens of start-up per track, and
is capped at $2 by `--max-budget-usd`. The quota guard stops background judging at 85% of the
week ([ADR 0053](adr/0053-judging-stops-at-85-percent-of-the-weekly-limit.md)).
