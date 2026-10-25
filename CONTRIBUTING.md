# Contributing

crateside is built for one workflow today (see the README) and is built with agentic engineering
using Claude Code. Outside help is welcome where it fits that.

## Issues

Use the Bug, Feature or Task template. Each asks what happens or is wanted, how it was seen or
measured, and what done looks like. An issue that says what a measurement counted is worth more
than one that says a number was wrong.

## Labels

| Label | Meaning |
| ----- | ------- |
| `needs-triage` | The maintainer has not evaluated it yet. New issues start here. |
| `needs-info` | Waiting on the reporter for more information. |
| `ready-for-agent` | Fully specified; a coding agent can take it without asking anything. |
| `ready-for-human` | The next step is a person's: a decision, an answer or hands-on work. An agent cannot advance it. |
| `needs-design` | Waiting on a design session; its result is committed before anyone builds. |
| `wontfix` | Will not be actioned. |
| `help wanted` | Outside help welcome; the issue is specified well enough to start. |
| `good first issue` | Small, self-contained, and a good way into the code. |

`ready-for-agent` and `ready-for-human` stay on purpose: the project is open about being worked
by agents, and the labels say who the next step belongs to. The full vocabulary is in
[docs/agents/triage-labels.md](docs/agents/triage-labels.md).

## Branches and pull requests

The maintainer commits to `master` directly and pushes it; CI runs on every push. Everyone else
forks and opens a pull request against `master`.

Before opening one:

```
dotnet test --solution Crateside.slnx
dotnet csharpier check .
```

Both must pass, with no build warnings (warnings are errors). `Crateside.Core` project-references
[AbleKit](https://github.com/Bamo16/AbleKit) as a sibling checkout, so clone it beside this repo.
Read [CLAUDE.md](CLAUDE.md) for the working agreements, [CONTEXT.md](CONTEXT.md) for the
vocabulary, and [docs/adr](docs/adr) before changing a decision.

Do not run the app while editing it, and do not add machine-specific paths.
