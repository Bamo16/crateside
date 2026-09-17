namespace Crateside.Core.Queue;

/// <summary>How to reach the Queue playlist.</summary>
/// <remarks>
/// The API key is a secret: put it in <c>appsettings.Secrets.json</c> or user-secrets, both
/// gitignored. The playlist ID is not a secret.
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

    /// <summary>OAuth client ID for a Desktop app client. Secret-adjacent.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret. A secret — user-secrets only, never the repo.</summary>
    public string? ClientSecret { get; set; }
}
