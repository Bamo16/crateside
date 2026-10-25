# Secrets

crateside holds two secrets, and they are worth very different amounts. A third is optional.

| secret | what it can do | where it lives |
|---|---|---|
| **YouTube API key** | Read a public/unlisted playlist. Cannot write. Quota-capped, revocable, restrictable to the YouTube Data API. | user-secrets |
| **OAuth refresh token** | **Delete items from the user's playlist.** | DPAPI-encrypted at rest — see below |
| **Judge API key** (optional) | Spend money on the judge's endpoint. Only needed to judge somewhere other than Claude Code's own login; see [judge.md](judge.md#the-endpoint). | user-secrets |

**Nothing secret is ever committed.** `.gitignore` covers `token.json`, `*.token.json`,
`client_secret*.json`, `appsettings.Secrets.json` and `appsettings.*.Local.json`, but the
first line of defense is that the key is not in the working tree at all.

## Setting the API key

From the repo root:

```powershell
dotnet user-secrets set "Queue:ApiKey" "<your key>" --project src\Crateside\Crateside.csproj
```

The section is still called `Queue` though the classes that read it are `Playlist…`: renaming it
would strand every key already set.

**The playlist ID is not a secret and does not go here** — set it on the Settings page. It is
saved to `%APPDATA%\crateside\queue.json`, which also keeps it out of the working tree, but
for tidiness rather than secrecy. The key is the only thing user-secrets holds.

`set` takes exactly `<key> <value>` positionally — the project goes in `--project`, and it
must be the **csproj**, not `Crateside.slnx` (user-secrets resolves a project, not a
solution). Quote the value, or PowerShell reads a leading `-` as a parameter. Running from
`src\Crateside` lets you drop `--project` entirely.

It is stored at `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`. The
`UserSecretsId` is in `Crateside.csproj`; the value is not.

**Why user-secrets rather than a gitignored file in the repo:** the risk being defended
against is showing the key by accident while working on this code — opening the wrong file,
or a file tree, on stream. A gitignored file is still a file sitting in the folder you have
open. user-secrets is outside the working tree, so it cannot appear.

To read it back or clear it: `dotnet user-secrets list` / `dotnet user-secrets remove "Queue:ApiKey"`.

**Also restrict the key in the Google Cloud console** — limit it to the YouTube Data API v3.
It costs nothing and caps the blast radius of a leak to burned quota.

`appsettings.Secrets.json` is still read if present, as a gitignored fallback for running
outside the Development environment. Prefer user-secrets.

## The OAuth client

A **Desktop app** client, created in the same Cloud project. The ID is secret-adjacent and
the secret plainly is, so both go to user-secrets:

```powershell
dotnet user-secrets set "Queue:ClientId" "<id>" --project src\Crateside\Crateside.csproj
dotnet user-secrets set "Queue:ClientSecret" "<secret>" --project src\Crateside\Crateside.csproj
```

Scope is `youtube` — the narrowest that permits `playlistItems.delete`.

## The refresh token

user-secrets is the wrong tool for the token: it is written by the app at runtime, not set by
hand, and it is the credential that can actually delete things. It is encrypted at rest with
**Windows DPAPI**, via ASP.NET Core's own Data Protection — no extra package, no P/Invoke.
`ProtectedTokenStore` implements Google's `IDataStore` (replacing its plaintext
`FileDataStore`) and writes to `%APPDATA%\crateside\tokens`; the key ring lives in
`%LOCALAPPDATA%\crateside\keys`, sealed to the current Windows user.

A token this key ring cannot decrypt — rotated keys, a file copied from another machine —
reads as **absent** rather than throwing. The cost is one more consent prompt, which is a
state crateside already handles (Settings offers *Grant access* again); a crash is not. There are tests for that, and for the fact
that nothing recognizable appears in the file on disk.

**Know what that does and does not buy.** DPAPI `CurrentUser` means the ciphertext is useless
on another machine or under another account — it defends against the file being copied off
the box or read by another user. It does **not** defend against anything running as you.
And no encryption helps if the app renders the secret on screen, so crateside never displays
either secret in its UI.
