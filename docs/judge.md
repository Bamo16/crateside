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
- **Complete evidence**: a song is judged only once its evidence holds a reading from every current detector, on keydetect's pinned build; crateside reads the missing ones first, and a song it cannot complete is failed, not judged ([ADR 0068](adr/0068-a-song-is-judged-only-on-complete-evidence.md)).
- **Tools**: `Read`, `Write`, `WebSearch` and `WebFetch`, nothing else, with Claude Code's tool
  search off (`ENABLE_TOOL_SEARCH=false`) so each schema is loaded up front rather than deferred.
  Its working directory is the track's working set, where the verdict file goes.

## Pages fetched before the session

Before a track's first session crateside looks the song up by artist and title (the ledger's
names for a song judged before, otherwise the row's `Artist - Title` without a bracketed tail)
and puts what it finds in the work order:

| site | requests | what is kept |
|---|---|---|
| Hooktheory | the TheoryTab at `theorytab/view/<artist>/<title>`, then up to three more the site's search files under the artist and title; each section's score from the site's API | each section's key, mode, tempo and Roman numerals; the contributors and how much of the synced video the sections span; from each score, its key changes. A section that changes key shows its chords by name, read by the key in force, since the page's numerals stay in the section's first key |
| Musicnotes | the search, then the first product filed under the artist and title themselves | the Original Published Key and the arrangement |
| Ultimate Guitar | the search | up to three of the song's tabs: those that state a key, then the most-voted chord charts |

A page that is not there, or a site that cannot be reached, is listed as such. The lookup is made
once per track, with the user agent `crateside/1.0`, and kept as `sources.json` beside the
evidence. crateside matches names only; whether a page is the right recording is the judge's
call, and the judge may still search.

## The verdict file

`verdict.json` in the working set, UTF-8 JSON in the shape the instructions give. crateside
fills in what the session cannot know: the time, the upload, the input file, the evidence, and
the song identity (kept from an earlier run of the same upload). It is checked against the
[ledger's rules](ledger.md#what-is-refused) before anything is recorded. A file that is not
valid JSON is read again with each raw control character inside a string escaped (a newline
written into `reasoning`), and refused only if it still does not parse.

A session that ends without a usable file is **resumed once** (`--resume` on the same session)
with "write the verdict file now" and what was wrong. A second failure is the track's: the
verdict stage counts it as a strike.

**Checked against the evidence** (ADR 0069), after the ledger's rules: the center's key must appear
in what the session was given or wrote (readings, runner-ups and key events, fetched pages, sources,
candidates, sections; a mode collapses to its major or minor), or the file is refused like any
other. A HIGH must be backed as the prompt asks: two of keydetect, madmom and Essentia on the
center's key, a tonic check, a human source that agrees, and no recorded authoritative source
stating another key (a Hooktheory page the work order marked thin excepted). After the one resume it is recorded
as MEDIUM, with a line in its reasoning saying it was capped and why.

**Windows.** A WITHHELD verdict may ask for up to four windows, in seconds. crateside detects
over each, resumes the same session once with that evidence, and records whatever it writes
then. If nothing usable follows, the WITHHELD verdict is recorded as it was. The windows
detected over are kept with the run, and their evidence inside the run's evidence under
`windows`. A window the judge asked for keeps its own one-sentence `why`, and the pass's reasoning is kept once on the run that follows it as `WithheldReasoning`.
Windows are detected over the full mix from the source index: the working set's download, else
the source the library keeps. A re-judge with neither downloads the upload again first. A run that
still has no audio says so in its work order, so the judge neither asks for windows nor withholds
waiting for them.

**Section times.** Each section gives a `start`, in seconds into the track, read from the times of keydetect's key events (ADR 0064). A section without one is logged and kept; a negative one is refused. Verdicts from before it have none.

**The text fields** have one job each (ADR 0064): `tonicCheck` is what the music does (at most 70 words), `reasoning` what the evidence is worth (at most 100 words in at most 2 paragraphs), `candidates` the 0 to 3 other keys weighed, each with a sentence, and a window's `why` one sentence. They are plain text with a blank line between paragraphs. An overrun is logged by the verdict run and never refused, since resuming a session to shorten it costs its start-up; a candidate equal to the verdict's key is dropped and logged. The screen typesets the text when it renders (`JudgeProse`), so old verdicts read the same way without being judged again.

**Judging from these bars** (#116). On a track's page the user gives windows themselves: bar
ranges from their warp, numbered as Live's ruler numbers them from the warp's beat 0 (ADR 0072)
and converted to seconds through the warp of the sample Ableton shows, or
minutes and seconds where there is no warp, each with an optional label, plus a note to the judge
and what they hear. A track with no stems has no warp view, so the same form shows on its page in
minutes and seconds, and left empty it judges the whole track again. crateside detects over each window before the first session, so the work
order carries them with their evidence and asks for one section per window; no resume is needed
for them. The windows are recorded with `AskedBy = User` and their bars, the note as the run's
`Direction`, and what they hear as a source of kind `ear` that crateside writes itself, noting how
much it counted. The judge may still ask for windows of its own, which are run as above.

The run needs the full mix: the working set's audio, or the source kept in the library, or else
the upload downloaded again (`stemforge-cli download`, no separation). A track whose working set
has gone is detected over whole first, into a working set made for the run and thrown away after
it. It is the user's own run, so it ignores the pause and the 85% guard, and
waits for any judgment in progress.

## Settings

Under `Judge` in `appsettings.json`, user-secrets or the environment (`Judge__Model`):

| setting | default | what it does |
|---|---|---|
| `Model` | `sonnet` | passed to `claude --model` |
| `Ear` | `Hint` | how much the user's own reading of a key counts: `Hint` (weighed, never raising confidence by itself) or `HumanSource` (counted like an authoritative source). Settings → **Your ear** overrides it, and is read on every run |
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

Setting any of `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY` or `ANTHROPIC_AUTH_TOKEN` takes the judge
off the weekly Claude limit: the quota guard never holds it, and with Library backfill on, ordinary
judging works the backlog too ([ADR 0067](adr/0067-a-judge-off-the-claude-limit-is-not-held-by-it.md)).
When the endpoint refuses a session (a rate limit, an overload, a server error, or no answer), judging
holds for the wait the endpoint states, up to an hour, or else a minute doubling to thirty, until a
session is served; the song is not failed for it
([ADR 0070](adr/0070-a-refusing-endpoint-holds-judging-with-a-backoff.md)). A spent Claude allowance
still pauses judging.
Claude Code sends its claude.ai login in the `Authorization` header unless `ANTHROPIC_AUTH_TOKEN`
replaces it, so a local endpoint that checks that header wants `ANTHROPIC_AUTH_TOKEN`, not
`ANTHROPIC_API_KEY`.

Every session runs with `--safe-mode`, so the user's own CLAUDE.md, rules and memory never reach
the judge, whichever endpoint it runs on.

## What it costs

On Claude Code's own login, each session and each resume spends Claude quota, about 37k tokens of start-up per track, and
is capped at $2 by `--max-budget-usd`. The quota guard stops background judging at 85% of the
week ([ADR 0053](adr/0053-judging-stops-at-85-percent-of-the-weekly-limit.md)), except in the
weekly spend-down, which judges the backlog to its own ceiling, 100% by default, in the last three hours
before the reset ([ADR 0063](adr/0063-the-backlog-is-judged-in-a-weekly-spend-down.md)).
