# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the
codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root — a glossary and nothing else.
- **`docs/adr/`** — read the ADRs that touch the area you are about to work in.

Single-context repo: one `CONTEXT.md`, one `docs/adr/`, no `CONTEXT-MAP.md` and no
context-scoped ADR directories.

## Each document holds one kind of content

`CLAUDE.md` states this rule and is always loaded, so it is not restated here — follow it. It
governs what may be written where: ADRs hold history, `CONTEXT.md` holds current definitions,
`CLAUDE.md` holds current instruction, code holds what the code cannot say itself. Writing
history or rationale into the glossary breaks it.

## Say whether a claim is measured, owned, or guessed

Also `CLAUDE.md`'s, and the rule this repo enforces hardest. Before writing a claim into an ADR,
`CONTEXT.md`, an issue or a comment, establish which of the three it is and label it. Silence is
not agreement: a suggestion the user did not contradict is still a suggestion.

Where a question can be settled by running the real code against the real state, do that instead
of arguing it. Many tests here assert against the live library and skip when it is absent.

## Use the glossary's vocabulary

When your output names a domain concept — an issue title, a refactor proposal, a hypothesis, a
test name — use the term as `CONTEXT.md` defines it, and do not drift to synonyms it explicitly
avoids.

If the concept you need is not in the glossary, that is a signal: either you are inventing
language the project does not use, or there is a real gap worth `/grill-with-docs`.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it rather than silently overriding:

> _Contradicts ADR 0011 (the audit may only claim what crateside wrote) — but worth reopening
> because…_

ADRs here are append-only, dated, and superseded in place. Correct one by adding to it, never by
pruning what it said.
