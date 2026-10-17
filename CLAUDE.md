# crateside — working agreements

## Git

**Commit directly to `master` and push to `origin`.** No PR flow, no branch, and do not ask
each time. This is solo work. Finished work is committed *and pushed*, not left sitting local.
This holds in a cloud session too. A `claude/...` branch the session assigns is not used:
commit on `master` and push nothing else.

## Tests and tooling

```
dotnet test --solution Crateside.slnx      # not --nologo: it is forwarded to the MTP app and rejected
dotnet csharpier check .                   # must be clean before every commit
```

Many tests **assert against the real library** — the Ableton roots under `P:\RYAN\Ableton`, the
ledger, this machine's working sets — and skip when those are absent. That has caught
things fixtures could not, repeatedly. Prefer a real-data test over a mock when the question is
"what is actually true on this machine".

## The AbleKit library

Reading Ableton's files is its own repo, `Bamo16/AbleKit` (formerly AbletonSampleData), checked out
beside this one at `V:\Repos\AbleKit` and project-referenced from `Crateside.Core`. It is on
NuGet as a preview, but crateside stays on the project reference until AbleKit's public API settles
(its `PublicAPI.Shipped.txt` stops being empty) or crateside gains a CI that cannot see the sibling
checkout (decided 2026-10-03). Library changes are committed there, on `main`, with its own CI. Crateside's interpretation
of Live's data (key spelling, genre, the keyword whitelist) stays in `src/Crateside.Core/Ableton/`.
Nothing goes public with the user's surname on it: see memory `keep-real-name-off-public-work`.

## Do not run the app while editing it

**Never start `watch` (or leave it running) during a series of edits.** It is not autostarted,
deliberately, and the task file says why.

`dotnet watch` hot-reloads: it patches the *running* process rather than restarting it. It can
update method bodies but cannot reshape objects that already exist — so adding a field to a
singleton leaves the live instance without it while the patched code reaches for it. The result
is a `NullReferenceException` on every tick of every drain loop, reported as **"Hot reload
succeeded"**. A multi-file change also lands one file at a time, so the process runs genuinely
inconsistent code in between.

The workflow that works:

- **While editing: nothing running.** Verify with `dotnet test --project tests/Crateside.Tests/Crateside.Tests.csproj`
  and, for the web project, a compile to a throwaway path:
  `dotnet build src/Crateside/Crateside.csproj -p:BaseOutputPath=<scratch>/`.
  Building normally while the app is up fails on a locked `Crateside.exe` and can wedge `watch`.
- **To exercise the pipeline: the `run` task**, started by the user. One clean process that will
  not restart under a download or a separation.
- **`watch` is for UI work only** — razor and CSS, where hot reload is genuinely good and nothing
  long-running is in flight.

**Do not launch the app yourself.** Ask the user to run the task, so output lands in their
terminal where they can see it and stop it.

## The GPU is the user's to spend

Separation is ~6–7 minutes of GPU per track and **more than one at a time makes the machine
unusable**. Never start a GPU-capable process without asking. Separation and judging (which spends
Claude quota, ~37k tokens of session start-up per track) run as soon as the app starts unless the
user paused them, and a pause of theirs survives a restart.

Detection is not in this category — it contends for nothing and runs unattended by design.

## Measure before recording

This project has repeatedly acted on premises nobody checked, and has twice written an unchecked
premise into the *correction* of another one. Before writing a claim into an ADR, `CONTEXT.md`,
or a comment, establish whether it is measured, owned, or guessed — and say which.

Silence is not agreement: a suggestion the user did not contradict is still a suggestion.

When something misbehaves, **test it rather than reasoning about it from outside**. Two wrong
diagnoses in one session were settled in minutes by running the real code against the real
state.

**A measurement is a premise too.** Ask what the script counted before believing the number —
one that overstated the verdict stage by 2× survived into a handoff unchallenged
([ADR 0006](docs/adr/0006-a-verdict-session-costs-turns-times-context.md)). Reading token cost
out of a session transcript: group `message.usage` by `requestId`, because one API response is
several rows and each carries the same usage object.

## Documents hold one kind of content each

Each kind of document holds one kind of content:

- **`docs/adr/`** holds history. Append-only, dated, superseded in place, never pruned.
- **`CONTEXT.md`** holds current definitions. A glossary and nothing else.
- **This file** holds current instruction, plus at most a one-line pointer to an ADR.
- **`docs/agents/`** holds how an agent should use this repo — where issues live, the triage
  label vocabulary, the domain doc layout. Not domain content itself.
- **`docs/*.md`** holds how-to and reference for a human (setup, secrets, the manual workflow),
  kept current and edited in place. A decision found in one moves to an ADR.
- **`docs/notes/`** holds research and observations, named by date and frozen like an ADR, but
  deciding nothing. A note that settles something is cited by the ADR that does the deciding.
- **`docs/design/`** holds the Claude Design round trip: see `docs/agents/design.md`.
- **Code** holds what the code cannot say itself. See below.

## Comments are the exception, not the default

Write the code so it says what it does. A comment earns its place only by saying something the
code cannot. **When in doubt, no comment.**

- **Nothing requires a doc comment** — not public members, not records, not properties. Most
  members should have none. Write a `<summary>` only where the name does not already say it.
- **A `<summary>` is one sentence and at most two lines.** Needing a third is the member asking to
  be split, not the summary asking for more room.
- **No `<remarks>`, ever.** A caveat that would cause a bug if missed goes in the `<summary>`, within
  its limit, or in a one-line `//` at the place the bug would happen. An ADR pointer goes at the
  end of the summary: `… (ADR 0027).` Anything that fits neither is narration, and belongs in a
  commit message or an ADR.
- **No `<param>`, `<returns>` or `<exception>`** unless it carries something the signature cannot
  — a unit, a range, a null that means something. The name is the documentation.
- **Never narrate.** How a bug was found, what was tried, what a measurement was on a given day,
  why an earlier design was wrong, why an alternative was rejected — none of it belongs in code.
  It goes in the commit message, or in an ADR if it is a decision. Before cutting an argument out
  of a comment, check it is already in one of those two — if it is not, move it there first.
- **Never restate the signature.** `/// <summary>The video ID.</summary>` on `VideoId` is noise.
- **Inline comments face the same bar.** If one is genuinely needed, make it one short line saying
  *what*, not why the design is what it is. A statement that needs a comment to be understood is
  usually a statement to rewrite.

**The existing code is not the standard here.** Much of it predates this rule and is being cut
back; matching its comment density re-creates the debt.

## Agent skills

### Issue tracker

GitHub Issues on `Bamo16/crateside`, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical names, unchanged. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` and `docs/adr/` at the root, See `docs/agents/domain.md`.

### Design

Page design goes to a Claude Design session, which reads the repo but cannot write to it or see
issues. Briefs out and results back: see `docs/agents/design.md`.

## This repository is going public

The maintainer intends to publish it ([#107](https://github.com/Bamo16/crateside/issues/107)).
Until then, new work must not add to what has to be undone: no new machine-specific paths, no new
dependency on a sibling checkout, no surname or personal circumstances anywhere (handle `Bamo16`
only), paid services behind a switchable interface, nothing listening beyond localhost without
authentication. Write issues and docs in the public voice ("the maintainer"). The full list and
the pre-publication checklist are in #107.
