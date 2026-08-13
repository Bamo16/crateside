using System.Text;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.DataProtection;

namespace Crateside.Core.Queue;

/// <summary>
/// Google's token store, encrypted at rest with Windows DPAPI.
/// </summary>
/// <remarks>
/// <para>
/// The refresh token is the credential that can <em>delete from the user's playlist</em>, so
/// unlike the read-only API key it does not sit in plaintext. Google's own
/// <c>FileDataStore</c> writes bare JSON; this is the same shape with the payload run through
/// ASP.NET Core Data Protection, whose key ring is in turn sealed to the current Windows user
/// via <c>ProtectKeysWithDpapi</c>.
/// </para>
/// <para>
/// <strong>What that does and does not buy.</strong> The ciphertext is useless on another
/// machine or under another Windows account, so it defends against the file being copied off
/// the box or read by another user. It does <em>not</em> defend against anything running as
/// this user. See <c>docs/secrets.md</c>.
/// </para>
/// <para>
/// Unreadable or undecryptable data is treated as absent rather than fatal: the worst case is
/// one more consent prompt, which is a state the Queue already handles. That also makes a key
/// ring rotation a non-event instead of a crash.
/// </para>
/// </remarks>
public sealed class ProtectedTokenStore(IDataProtectionProvider provider, string? directory = null)
    : IDataStore
{
    private const string Purpose = "crateside.queue.oauth.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    private readonly string _directory =
        directory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "crateside",
            "tokens"
        );

    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(_directory);

        var json = Google.Apis.Json.NewtonsoftJsonSerializer.Instance.Serialize(value);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));

        return File.WriteAllBytesAsync(PathFor(key), protectedBytes);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = PathFor(key);

        if (!File.Exists(path))
            return default!;

        try
        {
            var plaintext = _protector.Unprotect(await File.ReadAllBytesAsync(path));

            return Google.Apis.Json.NewtonsoftJsonSerializer.Instance.Deserialize<T>(
                Encoding.UTF8.GetString(plaintext)
            );
        }
        catch (Exception)
        {
            // Corrupt, or protected by a key ring this machine/user no longer has. Re-consent.
            return default!;
        }
    }

    public Task DeleteAsync<T>(string key)
    {
        File.Delete(PathFor(key));

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return Task.CompletedTask;
    }

    /// <summary>Whether any token is stored at all — "have we ever consented?".</summary>
    public bool HasStoredToken =>
        Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any();

    // Google's keys are safe filename characters in practice, but they are not guaranteed to
    // be, and a key containing a separator would otherwise write outside the directory.
    private string PathFor(string key) =>
        Path.Combine(
            _directory,
            string.Concat(key.Split(Path.GetInvalidFileNameChars())) + ".protected"
        );
}
