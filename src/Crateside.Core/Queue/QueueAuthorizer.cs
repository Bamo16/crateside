using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// Holds the OAuth credential that authorizes playlist deletions. A lapsed token is an ordinary
/// state: it expires about weekly for an unapproved app.
/// </summary>
public sealed class QueueAuthorizer(
    IOptions<QueueOptions> options,
    ProtectedTokenStore tokenStore,
    QueueWriteAccess access
)
{
    /// <summary>The narrowest scope that permits <c>playlistItems.delete</c>.</summary>
    private static readonly string[] Scopes = [YouTubeService.Scope.Youtube];

    private const string UserKey = "crateside";

    private readonly QueueOptions _options = options.Value;

    /// <summary>Whether an OAuth client is configured at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    /// <summary>Whether consent has ever been given and a token stored.</summary>
    public bool HasStoredToken => tokenStore.HasStoredToken;

    /// <summary>Whether writing still works, and what happened when it last did not.</summary>
    public QueueWriteAccess WriteAccess => access;

    /// <summary>
    /// The stored credential, refreshed if needed, or null when there is none or it cannot be.
    /// Never prompts: that is <see cref="ConsentAsync"/>.
    /// </summary>
    public async Task<UserCredential?> GetCredentialAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!IsConfigured || !HasStoredToken)
            return null;

        try
        {
            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
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

            access.Works();

            return credential;
        }
        catch (Exception e)
        {
            // A revoked or expired refresh token throws here. It still reads as "no credential"
            // to every caller — but it is not the same as never having consented, and collapsing
            // the two is what made the lapse silent (#34).
            access.Lapsed(e.Message);

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

    /// <summary>
    /// The YouTube channel the stored token belongs to, or null if unknown — the wrong one fails
    /// deletes as a bare 403. Costs 1 quota unit.
    /// </summary>
    public async Task<string?> GetAuthorizedChannelAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (await GetCredentialAsync(cancellationToken) is not { } credential)
            return null;

        try
        {
            using var service = new YouTubeService(
                new BaseClientService.Initializer
                {
                    ApplicationName = "crateside",
                    HttpClientInitializer = credential,
                }
            );

            var request = service.Channels.List("snippet");
            request.Mine = true;

            var response = await request.ExecuteAsync(cancellationToken);

            return response.Items?.FirstOrDefault()?.Snippet?.Title;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ClientSecrets ClientSecrets =>
        new() { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret };
}
