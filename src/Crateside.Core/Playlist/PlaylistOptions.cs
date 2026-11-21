namespace Crateside.Core.Playlist;

/// <summary>
/// How to reach the Queue playlist. The API key is a secret, kept in user-secrets or the
/// gitignored <c>appsettings.Secrets.json</c>.
/// </summary>
public sealed class PlaylistOptions
{
    public const string SectionName = "Queue";

    /// <summary>The playlist the user adds tracks to as they think of them.</summary>
    public string? PlaylistId { get; set; }

    /// <summary>
    /// A read-only API key. Sufficient while the playlist is public or unlisted, and it never
    /// expires. It cannot write — deletion needs OAuth user consent.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>No playlist is read and none is reported unreadable, for an instance that only shows what it was given (ADR 0074).</summary>
    public bool Offline { get; set; }

    /// <summary>OAuth client ID for a Desktop app client. Secret-adjacent.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. A secret — user-secrets only, never the repo.</summary>
    public string? ClientSecret { get; set; }
}
