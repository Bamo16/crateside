using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// Holds the OAuth credential that authorizes playlist deletions.
/// </summary>
/// <remarks>
/// <para>
/// Reading needs no consent, so <strong>an expired or absent token is an ordinary state, not
/// an error</strong>: the Queue keeps listing and only the delete button goes away. The write
/// token lapses roughly weekly for an unapproved app and re-consent is interactive; that is
/// known and accepted, not something to design around.
/// </para>
/// <para>
/// Consent runs the installed-app flow, which opens a browser and listens on
/// <c>http://localhost:&lt;port&gt;/</c>. crateside is already a browser app on this machine,
/// so that redirect is first-class here rather than a workaround.
/// </para>
/// </remarks>
public sealed class QueueAuthorizer(IOptions<QueueOptions> options, ProtectedTokenStore tokenStore)
{
    /// <summary>
    /// The narrowest scope that permits <c>playlistItems.delete</c>. Not <c>YoutubeForceSsl</c>
    /// on its own habit — this is the write capability, so it is named explicitly and kept as
    /// small as the operation allows.
    /// </summary>
    private static readonly string[] Scopes = [YouTubeService.Scope.Youtube];

    private const string UserKey = "crateside";

    private readonly QueueOptions _options = options.Value;

    /// <summary>Whether an OAuth client is configured at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    /// <summary>Whether consent has ever been given and a token stored.</summary>
    public bool HasStoredToken => tokenStore.HasStoredToken;

    /// <summary>
    /// The stored credential, refreshed if needed, or null when there is none or it can no
    /// longer be refreshed. Never prompts — call <see cref="ConsentAsync"/> for that, so a
    /// page render can never launch a browser window.
    /// </summary>
    public async Task<UserCredential?> TryGetCredentialAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!IsConfigured || !HasStoredToken)
            return null;

        try
        {
            var flow = new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
                new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = ClientSecrets,
                    Scopes = Scopes,
                    DataStore = tokenStore,
                }
            );

            if (await flow.LoadTokenAsync(UserKey, cancellationToken) is not { } token)
                return null;

            var credential = new UserCredential(flow, UserKey, token);

            if (credential.Token.IsStale)
                await credential.RefreshTokenAsync(cancellationToken);

            return credential;
        }
        catch (Exception)
        {
            // A revoked or expired refresh token throws on refresh. That is the ordinary
            // weekly lapse, so it reads as "no credential" and the UI offers re-consent.
            return null;
        }
    }

    /// <summary>
    /// Runs the interactive consent flow, opening a browser. Only ever called from an explicit
    /// user action.
    /// </summary>
    public Task<UserCredential> ConsentAsync(CancellationToken cancellationToken = default) =>
        !IsConfigured
            ? throw new QueueUnavailableException("No OAuth client is configured.")
            : GoogleWebAuthorizationBroker.AuthorizeAsync(
                ClientSecrets,
                Scopes,
                UserKey,
                cancellationToken,
                tokenStore
            );

    /// <summary>Forgets the stored token, so the next write needs fresh consent.</summary>
    public Task SignOutAsync() => tokenStore.ClearAsync();

    private ClientSecrets ClientSecrets =>
        new() { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret };
}
