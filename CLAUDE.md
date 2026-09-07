# crateside — working agreements

## Git

**Commit directly to `master` and push to `origin`.** No PR flow, no branch, and do not ask
each time. This is solo work. Finished work is committed *and pushed*, not left sitting local.

## Tests and tooling

```
dotnet test --solution Crateside.slnx      # not --nologo: it is forwarded to the MTP app and rejected
dotnet csharpier check .                   # must be clean before every commit
```

Many tests **assert against the real library** — the Ableton roots under `P:\RYAN\Ableton`, the
keytag ledger, this machine's working sets — and skip when those are absent. That has caught
things fixtures could not, repeatedly. Prefer a real-data test over a mock when the question is
"what is actually true on this machine".

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
unusable**. Never start a GPU-capable process without asking. Draining starts paused; so does
judging, which spends Claude quota (~37k tokens of session start-up per track).

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

Same rule as keytag ([its ADR 0005](../keytag/docs/adr/0005-instructions-carry-no-history.md)):

- **`docs/adr/`** holds history. Append-only, dated, superseded in place, never pruned.
- **`CONTEXT.md`** holds current definitions. A glossary and nothing else.
- **This file** holds current instruction, plus at most a one-line pointer to an ADR.
- **Code** holds what the code cannot say itself. See below.

## Doc comments are short, and `<remarks>` has to earn its place

The point of a doc comment is the hover popup. Anything longer than fits there is costing the
reader more than it gives them.

- **`<summary>` on anything public, one or two lines.** What it is, or what it answers. A record
  with three properties should read as three lines and three summaries, not thirty lines.
- **`<remarks>` only for a caveat that would cause a bug if missed** — a value that is not what
  its name suggests, a guarantee that holds only under a condition, an ordering that matters.
  Three lines is a lot. If it is longer than the member it documents, it is in the wrong file.
- **Never narrate.** How a bug was found, what was tried, what a measurement was on a given day,
  why an earlier design was wrong — none of that belongs in code. It goes in the commit message,
  or in an ADR if it is a decision. A reader hovering a property is not asking for the story.
- **Never restate the signature.** `/// <summary>The video ID.</summary>` on `VideoId` is noise.

The same applies to inline comments: one earns its place by saying something the code cannot.
