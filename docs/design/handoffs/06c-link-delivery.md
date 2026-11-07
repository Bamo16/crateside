# Handoff 06c: delivering design results by link

Issue: #62 (workflow follow-up to 06b)

## What changes
The Design session can publish a file at a short-lived public URL (about an hour, a few fetches).
The user pastes that URL to the code agent instead of downloading a zip and passing a path.

## Add to docs/agents/design.md → "Back: the result"

> The result arrives in one of two ways:
>
> 1. **A link** the user pastes (preferred). Fetch it straight away, since it expires in about an
>    hour and allows only a few fetches:
>    `curl -sL -o design.zip "<url>"`. No auth is needed.
> 2. **A download** in the user's Downloads folder, named `crateside-design-NN*`. Take the newest
>    match for the brief's number; `-vN` beats no suffix.
>
> In both cases, unzip in the scratchpad and follow the zip's README.

## If the link has expired
Ask the user for a fresh link. The Design session can re-issue one from the same file.

## Test
This zip was delivered this way. If you are reading it, the route works. Report that on #62.
