namespace Crateside.Core.Queue;

/// <summary>
/// How to reach the Queue playlist. <strong>The API key is a secret — never commit it.</strong>
/// </summary>
/// <remarks>
/// Put the key in <c>appsettings.Secrets.json</c> or user-secrets, both gitignored. The
/// playlist ID is not a secret and can live in <c>appsettings.json</c>.
/// </remarks>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    /// <summary>The playlist the user adds tracks to as they think of them.</summary>
    public string? PlaylistId { get; set; }

    /// <summary>
    /// A read-only API key. Sufficient while the playlist is public or unlisted, and it never
    /// expires. It cannot write — deletion needs OAuth user consent.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>OAuth client ID for a <strong>Desktop app</strong> client. Secret-adjacent.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. A secret — user-secrets only, never the repo.</summary>
    public string? ClientSecret { get; set; }
}
