# Secrets

crateside holds two secrets, and they are worth very different amounts.

| secret | what it can do | where it lives |
|---|---|---|
| **YouTube API key** | Read a public/unlisted playlist. Cannot write. Quota-capped, revocable, restrictable to the YouTube Data API. | user-secrets |
| **OAuth refresh token** *(not built yet)* | **Delete items from the user's playlist.** | DPAPI-encrypted at rest — see below |

**Nothing secret is ever committed.** `.gitignore` covers `token.json`, `*.token.json`,
`client_secret*.json`, `appsettings.Secrets.json` and `appsettings.*.Local.json`, but the
first line of defence is that the key is not in the working tree at all.

## Setting the API key

From the repo root:

```powershell
dotnet user-secrets set "Queue:ApiKey" "<your key>" --project src\Crateside\Crateside.csproj
dotnet user-secrets set "Queue:PlaylistId" "<id>" --project src\Crateside\Crateside.csproj
```

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

## The refresh token (when OAuth lands)

user-secrets is the wrong tool for the token: it is written by the app at runtime, not set by
hand, and it is the credential that can actually delete things. It gets encrypted at rest with
**Windows DPAPI**, via ASP.NET Core's own Data Protection — no extra package, no P/Invoke:

```csharp
services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("crateside")
    .ProtectKeysWithDpapi();
```

`IDataProtector.Protect` / `.Unprotect` then encrypt the token itself, and the key ring is
sealed to the current Windows user.

**Know what that does and does not buy.** DPAPI `CurrentUser` means the ciphertext is useless
on another machine or under another account — it defends against the file being copied off
the box or read by another user. It does **not** defend against anything running as you.
And no encryption helps if the app renders the secret on screen, so crateside never displays
either secret in its UI.
